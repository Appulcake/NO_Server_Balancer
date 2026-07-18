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
    // ReSharper disable once CollectionNeverQueried.Local
    private static readonly List<Object> SavedModifiedAssets = new();
    private static readonly HashSet<int> ModifiedLaserInstances = new();
    private static readonly HashSet<int> PendingTrukInstances = new();
    
    [HarmonyPatch(typeof(Blueprinter.Plugin), nameof(Blueprinter.Plugin.RegisterAddressableOverrides))]
    [HarmonyPostfix]
    private static void RegisterAddressableOverridesPostfix(Blueprinter.Plugin __instance)
    {
        Plugin.Logger.LogInfo("[ServerBalancer] Applying balance changes...");
        AircraftPriceManager.DiscoverBindAndApply(__instance, Plugin.AircraftPricesConfig);
        // LoggingUtils.LogLoadedLasers(__instance);
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
        AnimationCurveExporter.ExportCsv(Path.Combine(Paths.PluginPath, "CurveComparison.csv"), Utils.DamageCurves, 0f,
            50000f, 501);
        
        var seenLasers = new HashSet<int>();
        var seenTurrets = new HashSet<int>();
        
        foreach (KeyValuePair<string, LoadedBundle> bundleEntry in blueprinter.bundleRegistry.BundlesByName)
        {
            var loadedBundle = bundleEntry.Value;
            
            if (loadedBundle?.AssetBundle == null)
                continue;
            
            var assetBundle = loadedBundle.AssetBundle;
            
            foreach (var assetName in assetBundle.GetAllAssetNames())
            {
                GameObject root;
                
                try
                {
                    root = assetBundle.LoadAsset<GameObject>(assetName);
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogDebug($"[ServerBalancer] Failed to load root asset: {ex.Message}");
                    continue;
                }
                
                if (!root)
                    continue;
                
                var isTrukLaserPrefab =
                    string.Equals(root.name, "truk_laser", StringComparison.OrdinalIgnoreCase) ||
                    assetName.EndsWith("/truk_laser.prefab", StringComparison.OrdinalIgnoreCase);
                
                
                if (!isTrukLaserPrefab)
                    continue;
                
                Plugin.Logger.LogDebug("[ServerBalancer] Found truk_laser");
                
                var lasers = root.GetComponentsInChildren<Laser>(true);
                var turrets = root.GetComponentsInChildren<Turret>(true);
                
                foreach (var laser in lasers)
                {
                    if (!laser)
                        continue;
                    
                    if (!seenLasers.Add(laser.GetInstanceID()))
                        continue;
                    
                    if (!string.Equals(laser.gameObject.name, "laser_barrel", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    Plugin.Logger.LogDebug("[ServerBalancer] Found laser_barrel");
                    
                    laser.fireDamage = 40f;
                    laser.damageAtRange = Utils.DamageCurves["New_LADS"];
                    laser.info.targetRequirements.maxRange = 25000f;
                    
                    SavedModifiedAssets.Add(root);
                    SavedModifiedAssets.Add(laser);
                }
                
                foreach (var turret in turrets)
                {
                    if (!turret)
                        continue;
                    
                    if (!seenTurrets.Add(turret.GetInstanceID()))
                        continue;
                    
                    if (!string.Equals(turret.gameObject.name, "laser_turret", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    Plugin.Logger.LogInfo("[ServerBalancer] Found laser_turret");
                    
                    turret.targetAssessmentInterval = 0.1f;
                    turret.aimSolver.simulationInterval = 0.1f;
                    turret.lockTime = 0.01f;
                    
                    SavedModifiedAssets.Add(root);
                    SavedModifiedAssets.Add(turret);
                }
            }
        }
    }
    
    private static IEnumerator WaitForTrukLaserAndApply(NetworkIdentity trukIdentity)
    {
        var trukInstanceId = trukIdentity.GetInstanceID();
        
        // Limit routine to only wait for 600 frames max after spawn-event
        const int maximumFrames = 600;
        
        for (var frame = 0; frame < maximumFrames; frame++)
        {
            if (!trukIdentity)
            {
                PendingTrukInstances.Remove(trukInstanceId);
                yield break;
            }
            
            var laser = Utils.FindMountedHighPowerLaser(trukIdentity.transform);
            
            if (laser && laser != null)
            {
                ApplyTrukLaserOverride(trukIdentity, laser);
                
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
    private static void ApplyTrukLaserOverride(NetworkIdentity trukIdentity, Laser laser)
    {
        var laserInstanceId = laser.GetInstanceID();
        
        if (!ModifiedLaserInstances.Add(laserInstanceId))
            return;
        
        laser.fireDamage = 45f;
        
        laser.damageAtRange = Utils.DamageCurves["New_70kW"];
    }
}