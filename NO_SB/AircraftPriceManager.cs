using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Blueprinter;
using HarmonyLib;
using UnityEngine;
using Object = object;

namespace NO_SB;

[HarmonyPatch]
internal static class AircraftPriceManager
{
    private const string ConfigSection = "Aircraft Values";
    
    private static readonly Dictionary<string, List<AircraftDefinition>> DefinitionsById =
        new(StringComparer.OrdinalIgnoreCase);
    
    private static readonly Dictionary<string, ConfigEntry<float>> ConfigEntriesById =
        new(StringComparer.OrdinalIgnoreCase);
    
    private static readonly HashSet<int> SeenInstanceIds = new();
    
    // Save changed assets to a one-way static variable to keep them alive so Unity doesn't clean them
    // Basically same idea as PRF's Blueprinter fix, just in case
    // ReSharper disable once CollectionNeverQueried.Local
    private static readonly List<Object> SavedModifiedAssets = new();
    
    private static bool _configReloadHooked;
    
    [HarmonyPatch(typeof(Blueprinter.Plugin), nameof(Blueprinter.Plugin.RegisterAddressableOverrides))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void RegisterAddressableOverridesPostfix(Blueprinter.Plugin __instance)
    {
        Plugin.Logger.LogDebug("==== Applying Aircraft price changes...");
        DiscoverBindAndApply(__instance, Plugin.AircraftPricesConfig);
        Plugin.Logger.LogDebug("==== Aircraft price changes complete.");
    }
    
    private static void DiscoverBindAndApply(Blueprinter.Plugin blueprinter, ConfigFile config)
    {
        var newlyDiscovered = 0;
        
        // Search all AircraftDefinitions, mainly meant for iterating base game stuff
        var loadedDefinitions = Resources.FindObjectsOfTypeAll<AircraftDefinition>();
        
        foreach (var definition in loadedDefinitions)
            if (RegisterDefinition(definition))
                newlyDiscovered++;
        
        // Extra search in Blueprinter's bundles/assets too
        foreach (KeyValuePair<string, LoadedBundle> bundleEntry in blueprinter.bundleRegistry.BundlesByName)
        {
            var bundleName = bundleEntry.Key;
            var loadedBundle = bundleEntry.Value;
            
            if (loadedBundle?.AssetBundle == null)
                continue;
            
            AircraftDefinition[] bundleDefinitions;
            
            try
            {
                bundleDefinitions =
                    loadedBundle.AssetBundle.LoadAllAssets<AircraftDefinition>() ?? Array.Empty<AircraftDefinition>();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    $"Failed loading AircraftDefinitions from bundle \"{bundleName}\": {ex}");
                
                continue;
            }
            
            foreach (var definition in bundleDefinitions)
                if (RegisterDefinition(definition))
                    newlyDiscovered++;
        }
        
        foreach (var aircraft in DefinitionsById.OrderBy(pair => pair.Key))
            EnsureConfigEntryAndApply(aircraft.Key, aircraft.Value, config);
        
        if (!_configReloadHooked)
        {
            config.ConfigReloaded += (_, _) => ApplyAllConfiguredPrices();
            
            _configReloadHooked = true;
        }
        
        config.Save();
        
        Plugin.Logger.LogDebug($"Discovered {newlyDiscovered} new AircraftDefinition objects, " +
                               $"{DefinitionsById.Count} unique aircraft identifiers");
    }
    
    private static bool RegisterDefinition(AircraftDefinition definition)
    {
        if (!definition)
            return false;
        
        var instanceId = definition.GetInstanceID();
        
        if (!SeenInstanceIds.Add(instanceId))
            return false;
        
        var aircraftId = GetAircraftId(definition);
        
        if (string.IsNullOrWhiteSpace(aircraftId))
        {
            Plugin.Logger.LogWarning(
                $"Ignoring AircraftDefinition \"{definition.name}\" because it has no usable identifier.");
            
            return false;
        }
        
        if (!DefinitionsById.TryGetValue(aircraftId, out var definitions))
        {
            definitions = new List<AircraftDefinition>();
            DefinitionsById.Add(aircraftId, definitions);
        }
        
        definitions.Add(definition);
        SavedModifiedAssets.Add(definition);
        
        return true;
    }
    
    private static string GetAircraftId(AircraftDefinition definition)
    {
        // Use AircraftDefinition's jsonKey as identifier, with extra fall-back on unitName and name
        if (!string.IsNullOrWhiteSpace(definition.jsonKey))
            return definition.jsonKey.Trim();
        
        if (!string.IsNullOrWhiteSpace(definition.name))
            return definition.name.Trim();
        
        if (!string.IsNullOrWhiteSpace(definition.unitName))
            return definition.unitName.Trim();
        
        return string.Empty;
    }
    
    private static void EnsureConfigEntryAndApply(string aircraftId, List<AircraftDefinition> definitions,
        ConfigFile config)
    {
        var representative = definitions.FirstOrDefault(definition => definition);
        
        if (!representative)
            return;
        
        if (!ConfigEntriesById.TryGetValue(aircraftId, out var configEntry))
        {
            // ReSharper disable once PossibleNullReferenceException
            var loadedDefaultValue = representative.value;
            var displayName = GetDisplayName(representative);
            
            var definitionsDisagree =
                definitions.Any(definition =>
                    definition &&
                    !Mathf.Approximately(
                        definition.value,
                        loadedDefaultValue));
            
            if (definitionsDisagree)
            {
                var encounteredValues = string.Join(
                    ", ",
                    definitions
                        .Where(definition => definition)
                        .Select(definition => definition.value)
                        .Distinct());
                
                Plugin.Logger.LogWarning(
                    $"Multiple definitions for \"{aircraftId}\" have different loaded prices: " +
                    $"{encounteredValues}. Using {loadedDefaultValue} as the generated config default.");
            }
            
            configEntry = config.Bind(ConfigSection, aircraftId, loadedDefaultValue,
                new ConfigDescription(
                    $"{displayName}. " +
                    $"Internal identifier: {aircraftId}. " +
                    $"Loaded default asset value: {loadedDefaultValue}."));
            
            ConfigEntriesById.Add(aircraftId, configEntry);
            
            var capturedAircraftId = aircraftId;
            var capturedEntry = configEntry;
            
            capturedEntry.SettingChanged += (_, _) =>
                ApplyPrice(capturedAircraftId, capturedEntry.Value);
        }
        
        ApplyPrice(aircraftId, configEntry.Value);
    }
    
    private static string GetDisplayName(AircraftDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.unitName))
            return definition.unitName.Trim();
        
        if (!string.IsNullOrWhiteSpace(definition.name))
            return definition.name.Trim();
        
        return "<Unnamed Aircraft>";
    }
    
    private static void ApplyPrice(string aircraftId, float configuredValue)
    {
        if (!DefinitionsById.TryGetValue(aircraftId, out var definitions))
            return;
        
        var validDefinitions = 0;
        var changedDefinitions = 0;
        
        foreach (var definition in definitions)
        {
            if (!definition)
                continue;
            
            validDefinitions++;
            
            if (!Mathf.Approximately(definition.value, configuredValue))
                changedDefinitions++;
            
            definition.value = configuredValue;
        }
        
        Plugin.Logger.LogInfo($"Applied price {configuredValue} to " +
                              $"\"{aircraftId}\" on {validDefinitions} definition(s); " +
                              $"{changedDefinitions} value(s) changed.");
    }
    
    private static void ApplyAllConfiguredPrices()
    {
        foreach (var entry in ConfigEntriesById) ApplyPrice(entry.Key, entry.Value.Value);
    }
}