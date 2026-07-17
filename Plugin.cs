using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Blueprinter;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NO_Server_Balancer;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.nikkorap.blueprinter")]
public class Plugin : BaseUnityPlugin
{
    public new static ManualLogSource Logger { get; private set; } = null!;
    
    internal static ConfigFile AircraftPricesConfig { get; private set; } = null!;
    
    private Harmony? Harmony { get; set; }
    
    private void Awake()
    {
        Logger = base.Logger;
        
        AircraftPricesConfig = new ConfigFile(
            Path.Combine(
                Paths.ConfigPath,
                $"{MyPluginInfo.PLUGIN_GUID}.AircraftPrices.cfg"),
            saveOnInit: true,
            Info.Metadata);
        
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
        Plugin.Logger.LogInfo("[ServerBalancer] Applying balance changes...");
        AircraftPriceManager.DiscoverBindAndApply(__instance, Plugin.AircraftPricesConfig);
        ApplyServerBalanceChanges(__instance);
    }
    
    private static void ApplyServerBalanceChanges(
        Blueprinter.Plugin blueprinter)
    {
        var seenLasers = new HashSet<int>();
        // var seenAircrafts = new HashSet<int>();
        
        var modifiedLasersCount = 0;
        // var modifiedAircraftsCount = 0;
        
        foreach (KeyValuePair<string, LoadedBundle> bundleEntry
                 in blueprinter.bundleRegistry.BundlesByName)
        {
            var bundleName = bundleEntry.Key;
            var loadedBundle = bundleEntry.Value;
            
            if (loadedBundle?.AssetBundle == null)
                continue;
            
            var assetBundle = loadedBundle.AssetBundle;
            
            // var aircraftDefinitions = assetBundle.LoadAllAssets<AircraftDefinition>();
            
            /*
            foreach (var aircraft in aircraftDefinitions)
            {
                var isTernion =
                    string.Equals(
                        aircraft.jsonKey,
                        "P_Trisurface1",
                        StringComparison.OrdinalIgnoreCase);
                
                if (!isTernion)
                    continue;
                
                aircraft.value = Plugin.TernionValue.Value;
                
                SavedModifiedAssets.Add(aircraft);
            }
            */
            
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
                    
                    var falloffSlope = (0f - 1f) / (50000f - 15000f);
                    
                    Keyframe[] keys =
                    [
                        new(0f, 1f, 0f, 0f),
                        new(15000f, 1f, 0f, falloffSlope),
                        new(50000f, 0f, falloffSlope, falloffSlope)
                    ];
                    
                    laser.damageAtRange = new AnimationCurve(keys);
                    
                    SavedModifiedAssets.Add(root);
                    SavedModifiedAssets.Add(laser);
                    
                    modifiedLasersCount++;
                    
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
}

internal static class AircraftPriceManager
{
    private const string ConfigSection = "Aircraft Values";

    /*
     * A single aircraft identifier can potentially have multiple loaded
     * AircraftDefinition objects. Apply the configured price to all of them.
     */
    private static readonly Dictionary<string, List<AircraftDefinition>>
        DefinitionsById =
            new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, ConfigEntry<float>>
        ConfigEntriesById =
            new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<int> SeenInstanceIds = new();

    /*
     * Retain loaded assets. This may not be strictly necessary for all
     * definitions, but is consistent with the lifetime issue already found
     * with Blueprinter assets.
     */
    private static readonly List<UnityEngine.Object>
        SavedModifiedAssets = new();

    private static bool _configReloadHooked;

    internal static void DiscoverBindAndApply(
        Blueprinter.Plugin blueprinter,
        ConfigFile config)
    {
        int newlyDiscovered = 0;

        /*
         * Search all AircraftDefinitions currently loaded by Unity.
         *
         * This should find the base-game definitions once their databases
         * and assets have finished loading.
         */
        AircraftDefinition[] loadedDefinitions =
            Resources.FindObjectsOfTypeAll<AircraftDefinition>();

        foreach (AircraftDefinition definition in loadedDefinitions)
        {
            if (RegisterDefinition(definition))
                newlyDiscovered++;
        }

        /*
         * Explicitly search Blueprinter bundles too. Some bundle assets might
         * not otherwise be visible through Resources.FindObjectsOfTypeAll.
         */
        foreach (KeyValuePair<string, LoadedBundle> bundleEntry
                 in blueprinter.bundleRegistry.BundlesByName)
        {
            string bundleName = bundleEntry.Key;
            LoadedBundle loadedBundle = bundleEntry.Value;

            if (loadedBundle?.AssetBundle == null)
                continue;

            AircraftDefinition[] bundleDefinitions;

            try
            {
                bundleDefinitions =
                    loadedBundle.AssetBundle
                        .LoadAllAssets<AircraftDefinition>() ??
                    Array.Empty<AircraftDefinition>();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    $"[AircraftPrices] Failed loading AircraftDefinitions " +
                    $"from bundle \"{bundleName}\": {ex}");

                continue;
            }

            foreach (AircraftDefinition definition in bundleDefinitions)
            {
                if (RegisterDefinition(definition))
                    newlyDiscovered++;
            }
        }

        /*
         * Create any missing dynamic entries, then apply all configured
         * values. Ordering is not necessary, but makes the generated config
         * file and logs more predictable.
         */
        foreach (KeyValuePair<string, List<AircraftDefinition>> aircraft
                 in DefinitionsById.OrderBy(pair => pair.Key))
        {
            EnsureConfigEntryAndApply(
                aircraft.Key,
                aircraft.Value,
                config);
        }

        if (!_configReloadHooked)
        {
            config.ConfigReloaded += (_, _) =>
                ApplyAllConfiguredPrices();

            _configReloadHooked = true;
        }

        config.Save();

        Plugin.Logger.LogInfo(
            $"[AircraftPrices] Discovered {newlyDiscovered} new " +
            $"AircraftDefinition objects; " +
            $"{DefinitionsById.Count} unique aircraft identifiers.");
    }

    private static bool RegisterDefinition(
        AircraftDefinition definition)
    {
        if (!definition)
            return false;

        int instanceId = definition.GetInstanceID();

        if (!SeenInstanceIds.Add(instanceId))
            return false;

        string aircraftId = GetAircraftId(definition);

        if (string.IsNullOrWhiteSpace(aircraftId))
        {
            Plugin.Logger.LogWarning(
                $"[AircraftPrices] Ignoring AircraftDefinition " +
                $"\"{definition.name}\" because it has no usable identifier.");

            return false;
        }

        if (!DefinitionsById.TryGetValue(
                aircraftId,
                out List<AircraftDefinition>? definitions))
        {
            definitions = new List<AircraftDefinition>();
            DefinitionsById.Add(aircraftId, definitions);
        }

        definitions.Add(definition);
        SavedModifiedAssets.Add(definition);

        return true;
    }

    private static string GetAircraftId(
        AircraftDefinition definition)
    {
        /*
         * jsonKey should be the most stable internal identifier.
         *
         * Asset name is a reasonable fallback. unitName should preferably
         * remain a display name rather than the persistent config identity.
         */
        if (!string.IsNullOrWhiteSpace(definition.jsonKey))
            return definition.jsonKey.Trim();

        if (!string.IsNullOrWhiteSpace(definition.name))
            return definition.name.Trim();

        if (!string.IsNullOrWhiteSpace(definition.unitName))
            return definition.unitName.Trim();

        return string.Empty;
    }

    private static void EnsureConfigEntryAndApply(
        string aircraftId,
        List<AircraftDefinition> definitions,
        ConfigFile config)
    {
        AircraftDefinition? representative =
            definitions.FirstOrDefault(definition => definition);

        if (!representative)
            return;

        if (!ConfigEntriesById.TryGetValue(
                aircraftId,
                out ConfigEntry<float>? configEntry))
        {
            float loadedDefaultValue = representative.value;
            string displayName = GetDisplayName(representative);

            bool definitionsDisagree =
                definitions.Any(definition =>
                    definition &&
                    !Mathf.Approximately(
                        definition.value,
                        loadedDefaultValue));

            if (definitionsDisagree)
            {
                string encounteredValues = string.Join(
                    ", ",
                    definitions
                        .Where(definition => definition)
                        .Select(definition => definition.value)
                        .Distinct());

                Plugin.Logger.LogWarning(
                    $"[AircraftPrices] Multiple definitions for " +
                    $"\"{aircraftId}\" have different loaded prices: " +
                    $"{encounteredValues}. Using {loadedDefaultValue} " +
                    $"as the generated config default.");
            }

            configEntry = config.Bind(
                ConfigSection,
                aircraftId,
                loadedDefaultValue,
                new ConfigDescription(
                    $"{displayName}. " +
                    $"Internal identifier: {aircraftId}. " +
                    $"Loaded asset value: {loadedDefaultValue}."));

            ConfigEntriesById.Add(aircraftId, configEntry);

            /*
             * Capture separate locals so this callback always refers to the
             * correct aircraft and config entry.
             */
            string capturedAircraftId = aircraftId;
            ConfigEntry<float> capturedEntry = configEntry;

            capturedEntry.SettingChanged += (_, _) =>
                ApplyPrice(
                    capturedAircraftId,
                    capturedEntry.Value);
        }

        ApplyPrice(aircraftId, configEntry.Value);
    }

    private static string GetDisplayName(
        AircraftDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.unitName))
            return definition.unitName.Trim();

        if (!string.IsNullOrWhiteSpace(definition.name))
            return definition.name.Trim();

        return "<unnamed aircraft>";
    }

    private static void ApplyPrice(
        string aircraftId,
        float configuredValue)
    {
        if (!DefinitionsById.TryGetValue(
                aircraftId,
                out List<AircraftDefinition>? definitions))
        {
            return;
        }

        int validDefinitions = 0;
        int changedDefinitions = 0;

        foreach (AircraftDefinition definition in definitions)
        {
            if (!definition)
                continue;

            validDefinitions++;

            if (!Mathf.Approximately(
                    definition.value,
                    configuredValue))
            {
                changedDefinitions++;
            }

            definition.value = configuredValue;
        }

        Plugin.Logger.LogInfo(
            $"[AircraftPrices] Applied price {configuredValue} to " +
            $"\"{aircraftId}\" on {validDefinitions} definition(s); " +
            $"{changedDefinitions} value(s) changed.");
    }

    private static void ApplyAllConfiguredPrices()
    {
        foreach (KeyValuePair<string, ConfigEntry<float>> entry
                 in ConfigEntriesById)
        {
            ApplyPrice(entry.Key, entry.Value.Value);
        }
    }
}