using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Blueprinter;
using HarmonyLib;
using Mirage;
using UnityEngine;
using Object = UnityEngine.Object;

// ReSharper disable InconsistentNaming

namespace NO_Server_Balancer;

[HarmonyPatch]
internal static class LASS
{
    // ReSharper disable once CollectionNeverQueried.Global
    public static readonly List<Object> SavedModifiedAssets = new();
    private static readonly HashSet<int> ModifiedLaserInstances = new();
    private static readonly HashSet<int> PendingTrukInstances = new();
    
    [HarmonyPatch(typeof(Blueprinter.Plugin), nameof(Blueprinter.Plugin.RegisterAddressableOverrides))]
    [HarmonyPostfix]
    private static void RegisterAddressableOverridesPostfix(Blueprinter.Plugin __instance)
    {
        Plugin.Logger.LogDebug("[ServerBalancer] Applying balance changes...");
        AircraftPriceManager.DiscoverBindAndApply(__instance, Plugin.AircraftPricesConfig);
        if (Plugin.IsDebug)
            LoggingUtils.LogLoadedLasers(__instance);
        ApplyServerBalanceChanges(__instance);
    }
    
    [HarmonyPatch(typeof(ServerObjectManager), nameof(ServerObjectManager.Spawn), typeof(NetworkIdentity))]
    [HarmonyPrefix]
    private static void ServerObjectManagerSpawnPrefix(NetworkIdentity __0)
    {
        if (!__0)
            return;
        
        if (!Utils.HasUnityName(__0.name, "kar_truk"))
            return;
        
        var trukInstanceId = __0.GetInstanceID();
        
        if (!PendingTrukInstances.Add(trukInstanceId))
            return;
        
        Plugin.Logger.LogDebug($"[ServerBalancer] Waiting for loadout on {__0.name}.");
        
        Plugin.Instance.StartCoroutine(WaitForTrukLaserAndApply(__0));
    }
    
    private static void ApplyServerBalanceChanges(Blueprinter.Plugin blueprinter)
    {
        // Create .csv to check damage fall-off of curves on a graph
        if (Plugin.IsDebug)
            Utils.ExportAnimationCsv(Path.Combine(Paths.PluginPath, "CurveComparison.csv"), Utils.DamageCurves, 0f,
                50000f, 501);
        
        var seenLasers = new HashSet<int>();
        
        foreach (KeyValuePair<string, LoadedBundle> bundleEntry in blueprinter.bundleRegistry.BundlesByName)
        {
            var loadedBundle = bundleEntry.Value;
            
            if (loadedBundle?.AssetBundle == null)
                continue;
            
            var assetBundle = loadedBundle.AssetBundle;
            
            foreach (var assetName in assetBundle.GetAllAssetNames())
            {
                GameObject parentVehicle;
                
                try
                {
                    parentVehicle = assetBundle.LoadAsset<GameObject>(assetName);
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[ServerBalancer] Failed to load root asset: {ex.Message}");
                    continue;
                }
                
                if (!parentVehicle)
                    continue;
                
                // Attempt at enabling turrets to track, seems to not work on servers so nvm for now
                SetTurretTracking.ApplyTracking(parentVehicle, assetName);
                
                var isTrukLaserPrefab =
                    string.Equals(parentVehicle.name, "truk_laser", StringComparison.OrdinalIgnoreCase) ||
                    assetName.EndsWith("/truk_laser.prefab", StringComparison.OrdinalIgnoreCase);
                
                if (!isTrukLaserPrefab)
                    continue;
                
                Plugin.Logger.LogDebug("[ServerBalancer] Found truk_laser");
                
                var lasers = parentVehicle.GetComponentsInChildren<Laser>(true);
                
                foreach (var laser in lasers)
                {
                    if (!laser)
                        continue;
                    
                    if (!seenLasers.Add(laser.GetInstanceID()))
                        continue;
                    
                    if (!string.Equals(laser.gameObject.name, "laser_barrel", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    Plugin.Logger.LogDebug($"[ServerBalancer] Found {parentVehicle.name}'s laser_barrel");
                    
                    laser.fireDamage = 40f;
                    laser.damageAtRange = Utils.DamageCurves["New_LADS"];
                    
                    SavedModifiedAssets.Add(parentVehicle);
                    SavedModifiedAssets.Add(laser);
                }
            }
        }
    }
    
    // Coroutine to wait (~ 1 or couple frames) for spawned truk to have its component attached
    // Since on spawn frame it doesn't have its children objects like turrets yet
    private static IEnumerator WaitForTrukLaserAndApply(NetworkIdentity trukIdentity)
    {
        var trukInstanceId = trukIdentity.GetInstanceID();
        
        // Limit routine to only wait for 600 frames max after spawn-event, exit early if found
        const int maximumFrames = 600;
        
        for (var frame = 0; frame < maximumFrames; frame++)
        {
            if (!trukIdentity)
            {
                PendingTrukInstances.Remove(trukInstanceId);
                yield break;
            }
            
            var laserhp = Utils.FindMountedLaser(trukIdentity.transform, "kar_turret_laserhp");
            var laser = Utils.FindMountedLaser(trukIdentity.transform, "kar_turret_laser");
            
            if (laser != null)
            {
                ApplyTrukLaserOverride(trukIdentity, laser, 0);
                
                PendingTrukInstances.Remove(trukInstanceId);
                yield break;
            }
            
            if (laserhp != null)
            {
                ApplyTrukLaserOverride(trukIdentity, laserhp, 1);
                
                PendingTrukInstances.Remove(trukInstanceId);
                yield break;
            }
            
            yield return null;
        }
        
        PendingTrukInstances.Remove(trukInstanceId);
        
        Plugin.Logger.LogWarning(
            $"[ServerBalancer] Timed out waiting for kar_turret_laserhp beneath {trukIdentity.name}.");
    }
    
    // ReSharper disable once UnusedParameter.Local
    private static void ApplyTrukLaserOverride(NetworkIdentity trukIdentity, Laser laser, int laserIndex)
    {
        var laserInstanceId = laser.GetInstanceID();
        
        if (!ModifiedLaserInstances.Add(laserInstanceId))
            return;
        
        switch (laserIndex)
        {
            case 0:
            {
                laser.fireDamage = 45f;
                laser.damageAtRange = Utils.DamageCurves["First_LADS"];
                break;
            }
            case 1:
            {
                laser.fireDamage = 30f;
                laser.damageAtRange = Utils.DamageCurves["New_70kW"];
                break;
            }
        }
    }
}