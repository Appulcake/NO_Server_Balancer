using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using Blueprinter;
using HarmonyLib;
using UnityEngine;
using Logger = UnityEngine.Logger;
using Object = UnityEngine.Object;

namespace NO_Server_Balancer;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.nikkorap.blueprinter")]
public class Plugin : BaseUnityPlugin
{
    public new static ManualLogSource Logger { get; private set; } = null!;
    
    private Harmony? Harmony { get; set; }
    
    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        Repatch();
    }
    
    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }
    
    private void Repatch()
    {
        Harmony?.UnpatchSelf();
        // Logger.LogInfo("[ServerBalance] Patching Harmony...");
        Harmony?.PatchAll();
    }
}

[HarmonyPatch]
internal static class LASS
{
    private static readonly List<Object> SavedModifiedAssets = new();
    
    [HarmonyPatch(typeof(Blueprinter.Plugin), nameof(Blueprinter.Plugin.RegisterAddressableOverrides))]
    [HarmonyPostfix]
    private static void RegisterAddressableOverridesPostfix(Blueprinter.Plugin __instance)
    {
        // Plugin.Logger.LogInfo("[ServerBalance] Applying balance changes...");
        // LogLoadedLasers(__instance);
        ApplyServerBalanceChanges(__instance);
    }
    
    private static void LogLoadedLasers(Blueprinter.Plugin blueprinter)
    {
        var seenLasers = new HashSet<int>();
        var foundCount = 0;
        
        foreach (KeyValuePair<string, LoadedBundle> bundleEntry
                 in blueprinter.bundleRegistry.BundlesByName)
        {
            var bundleName = bundleEntry.Key;
            var loadedBundle = bundleEntry.Value;
            
            if (loadedBundle?.AssetBundle == null)
                continue;
            
            var assetBundle = loadedBundle.AssetBundle;
            string[] assetNames;
            
            try
            {
                assetNames = assetBundle.GetAllAssetNames();
            }
            catch (Exception ex)
            {
                //Plugin.Logger.LogError(
                //    $"[ServerBalance] Could not enumerate bundle " +
                //    $"\"{bundleName}\": {ex}");
                
                continue;
            }
            
            foreach (var assetName in assetNames)
            {
                GameObject root;
                
                try
                {
                    root = assetBundle.LoadAsset<GameObject>(assetName);
                }
                catch (Exception ex)
                {
                    //Plugin.Logger.LogError(
                    //    $"[ServerBalance] Could not load GameObject asset " +
                    //    $"\"{assetName}\" from \"{bundleName}\": {ex}");
                    
                    continue;
                }
                
                // Many assets will not be GameObjects.
                if (!root)
                    continue;
                
                var lasers =
                    root.GetComponentsInChildren<Laser>(
                        true);
                
                foreach (var laser in lasers)
                {
                    if (!laser)
                        continue;
                    
                    // Avoid duplicate output if the same prefab can be reached
                    // through more than one loaded asset reference.
                    if (!seenLasers.Add(laser.GetInstanceID()))
                        continue;
                    
                    foundCount++;
                    
                    /*
                    Plugin.Logger.LogInfo(
                        $"[ServerBalance] Found Laser:" +
                        $"\n  Bundle: {bundleName}" +
                        $"\n  Asset: {assetName}" +
                        $"\n  Root: {root.name}" +
                        $"\n  Path: {GetTransformPath(laser.transform)}" +
                        $"\n  fireDamage: {laser.fireDamage}" +
                        $"\n  blastDamage: {laser.blastDamage}");
                        */
                }
            }
        }
        
        //Plugin.Logger.LogInfo(
        //    $"[ServerBalance] Found {foundCount} unique Laser components.");
    }
    
    private static void ApplyServerBalanceChanges(
        Blueprinter.Plugin blueprinter)
    {
        var seenLasers = new HashSet<int>();
        var modifiedCount = 0;
        
        foreach (KeyValuePair<string, LoadedBundle> bundleEntry
                 in blueprinter.bundleRegistry.BundlesByName)
        {
            var bundleName = bundleEntry.Key;
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
                    //Plugin.Logger.LogError(
                    //    $"[ServerBalance] Failed loading \"{assetName}\" " +
                    //    $"from \"{bundleName}\": {ex}");
                    
                    continue;
                }
                
                if (!root)
                    continue;
                
                var isTrukLaserPrefab =
                    string.Equals(
                        root.name,
                        "truk_laser",
                        StringComparison.OrdinalIgnoreCase) ||
                    assetName.EndsWith(
                        "/truk_laser.prefab",
                        StringComparison.OrdinalIgnoreCase);
                
                if (!isTrukLaserPrefab)
                    continue;
                
                var lasers =
                    root.GetComponentsInChildren<Laser>(
                        true);
                
                foreach (var laser in lasers)
                {
                    if (!laser)
                        continue;
                    
                    if (!seenLasers.Add(laser.GetInstanceID()))
                        continue;
                    
                    if (!string.Equals(
                            laser.gameObject.name,
                            "laser_barrel",
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    //var previousFireDamage = laser.fireDamage;
                    //var previousBlastDamage = laser.blastDamage;
                    
                    laser.fireDamage = 45f;
                    // laser.blastDamage = 1f;
                    
                    float falloffSlope = (0f - 1f) / (50000f - 15000f);
                    
                    Keyframe[] keys =
                    [
                        new(0f, 1f, 0f, 0f),
                        new(15000f, 1f, 0f, falloffSlope),
                        new(50000f, 0f, falloffSlope, falloffSlope)
                    ];
                    
                    laser.damageAtRange = new AnimationCurve(keys);
                    
                    SavedModifiedAssets.Add(root);
                    SavedModifiedAssets.Add(laser);
                    
                    modifiedCount++;
                    
                    /*
                    Plugin.Logger.LogInfo("LASER MUZZLE: {laser.");
                    
                    Plugin.Logger.LogInfo(
                        $"[ServerBalance] Modified " +
                        $"{GetTransformPath(laser.transform)}:" +
                        $"\n  fireDamage: {previousFireDamage} " +
                        $"-> {laser.fireDamage}" +
                        $"\n  blastDamage: {previousBlastDamage} " +
                        $"-> {laser.blastDamage}");
                        */
                        
                }
            }
        }
        
        /*
        if (modifiedCount == 0)
            Plugin.Logger.LogError(
                "[ServerBalance] Could not find the truk_laser " +
                "laser_barrel Laser component.");
        else
            Plugin.Logger.LogInfo(
                $"[ServerBalance] Modified {modifiedCount} matching " +
                "Laser component(s).");
                */
    }
    
    private static string GetTransformPath(Transform transform)
    {
        var names = new Stack<string>();
        var current = transform;
        
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }
        
        return string.Join("/", names);
    }
    
    /*
    [HarmonyPatch(typeof(AimSolver), nameof(AimSolver.GetAimVector))]
    [HarmonyPrefix]
    private static bool GetAimVectorPrefix(AimSolver __instance, out float targetRange, ref Vector3 __result)
    {
        float rakeFrequency = 0f;
        float rakeAmount = 0f;
        
        GlobalPosition globalPosition = __instance.firingTransform.GlobalPosition();
        GlobalPosition globalPosition2 = __instance.currentTarget.GlobalPosition();
        targetRange = FastMath.Distance(globalPosition2, globalPosition);
        Vector3 vector = ((__instance.currentTarget.speed < 1f) ? Vector3.zero : __instance.currentTarget.rb.velocity);
        Vector3 vector2 = ((__instance.attachedUnit.speed < 1f) ? Vector3.zero : __instance.attachedUnit.rb.velocity);
        float num = Vector3.Dot((globalPosition2 - globalPosition).normalized, vector2 - vector);
        float num2 = targetRange / (__instance.weaponInfo.GetMaxSpeed() * 0.9f + num);
        if (__instance.correctShots && __instance.observedBullet != null)
        {
            __instance.ObserveBullet();
        }
        if (__instance.weaponInfo.muzzleVelocity == 0f)
        {
            __result = globalPosition2 + num2 * vector - globalPosition;
            
            Plugin.Logger.LogError("No MuzzleVelicity Path");
            
            return false;
        }
        Vector3 target = (vector - __instance.targetVelPrev) / Time.fixedDeltaTime;
        __instance.targetVelPrev = vector;
        vector *= 1f + Mathf.Cos(Time.timeSinceLevelLoad * Mathf.PI * 2f * rakeFrequency) * rakeAmount;
        __instance.targetAccelSmoothed = Vector3.SmoothDamp(__instance.targetAccelSmoothed, target, ref __instance.targetAccelSmoothingVel, 0.5f);
        Vector3 vector3 = num2 * vector + 0.5f * num2 * num2 * __instance.targetAccelSmoothed;
        vector3 -= num2 * vector2;
        Vector3 vector4 = num2 * num2 * 4.905f * __instance.weaponInfo.gravMult * Vector3.up;
        Vector3 vector5 = globalPosition2 + vector3 + vector4 - globalPosition;
        __instance.RunSim(globalPosition, globalPosition2, vector5, vector, num2);
        __instance.simCorrectionSmoothed = Vector3.SmoothDamp(__instance.simCorrectionSmoothed, __instance.simCorrection, ref __instance.correctionSmoothingVel, 0.15f);
        __result = vector5 + __instance.simCorrection + __instance.aimCorrection;
        
        return false;
    }
    
    [HarmonyPatch(typeof(AimSolver), nameof(AimSolver.RunSim))]
    [HarmonyPrefix]
    private static bool RunSimPrefix(AimSolver __instance, GlobalPosition muzzlePosition, GlobalPosition targetPosition,
        Vector3 simpleLead, Vector3 targetVel, float estimatedTimeToTarget)
    {
        float simulationInterval = 0.05f;
        
        if (!(Time.timeSinceLevelLoad - __instance.lastSim < simulationInterval))
        {
            __instance.lastSim = Time.timeSinceLevelLoad;
            Vector3 initialVelocity = ((__instance.attachedUnit.speed > 1f) ? __instance.attachedUnit.rb.GetPointVelocity(__instance.firingTransform.position) : Vector3.zero) + simpleLead.normalized * __instance.weaponInfo.muzzleVelocity;
            __instance.simCorrection = -Kinematics.TrajectorySim(__instance.weaponInfo, initialVelocity, muzzlePosition, targetPosition, targetVel, __instance.targetAccelSmoothed, 0.1f, out var _);
            if (__instance.simCorrectionSmoothed == Vector3.zero)
            {
                __instance.simCorrectionSmoothed = __instance.simCorrection;
            }
        }
        
        return false;
    }
    */
    
    
    /*
    [HarmonyPatch(typeof(AimSolver), nameof(AimSolver.GetAimVector))]
    [HarmonyPrefix]
    private static bool GetAimVectorPrefix(
        AimSolver __instance,
        out float targetRange,
        ref Vector3 __result)
    {
        GlobalPosition muzzlePosition =
            __instance.firingTransform.GlobalPosition();
        
        GlobalPosition targetPosition =
            __instance.currentTarget.GlobalPosition();
        
        targetRange = FastMath.Distance(targetPosition, muzzlePosition);
        
        // Direct current line of sight: no lead, gravity, simulation,
        // acceleration, raking, or learned aim correction.
        __result = targetPosition - muzzlePosition;
        
        return false;
    }
    
    */
}