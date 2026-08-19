using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.SavedMission;

namespace NO_SB.MissionRestrictions;

[HarmonyPatch]
internal static class HarmonyPatches
{
    private static Mission? _preparedMission;
    private static string _missionIdentifier = string.Empty;
    
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.OnMissionLoad))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void FactionHQOnMissionLoadPostfix(FactionHQ __instance, Mission? mission)
    {
        if (!GameManager.IsHeadless || !__instance.IsServer || mission == null)
            return;
        
        PrepareMission(mission);
        
        var weaponRestrictions = new HashSet<string>(__instance.restrictedWeapons, StringComparer.Ordinal);
        var aircraftRestrictions = new HashSet<string>(__instance.restrictedAircraft, StringComparer.Ordinal);
        var factionName = __instance.faction.factionName;
        
        var weaponsAddedByPlugin = 0;
        var aircraftAddedByPlugin = 0;
        
        foreach (var weaponName in WeaponRestrictions.GetRestrictedWeaponNames(_missionIdentifier, factionName))
        {
            if (weaponRestrictions.Add(weaponName))
                weaponsAddedByPlugin++;
        }
        
        foreach (var aircraftId in AircraftRestrictions.GetRestrictedAircraftIds(_missionIdentifier, factionName))
        {
            if (aircraftRestrictions.Add(aircraftId))
                aircraftAddedByPlugin++;
        }
        
        __instance.NetworkrestrictedWeapons = weaponRestrictions.ToList();
        __instance.NetworkrestrictedAircraft = aircraftRestrictions.ToList();
        
        Plugin.Logger.LogInfo(
            $"Applied mission restrictions to {factionName} for \"{_missionIdentifier}\": " +
            $"{weaponsAddedByPlugin} plugin weapon restriction(s), " +
            $"{aircraftAddedByPlugin} plugin aircraft restriction(s); " +
            $"{weaponRestrictions.Count} total weapon restriction(s), " +
            $"{aircraftRestrictions.Count} total aircraft restriction(s).");
    }
    
    private static void PrepareMission(Mission mission)
    {
        if (ReferenceEquals(_preparedMission, mission))
            return;
        
        _preparedMission = mission;
        
        ReloadOrRecreateConfig(Plugin.WeaponRestrictionsConfig);
        ReloadOrRecreateConfig(Plugin.AircraftRestrictionsConfig);
        
        _missionIdentifier = MissionIdentifier.Get(mission);
        
        var newWeapons = WeaponRestrictions.RefreshConfig();
        var newAircraft = AircraftRestrictions.RefreshConfig();
        
        Plugin.Logger.LogInfo(
            $"Preparing global restrictions for mission " +
            $"\"{mission.Name}\" - matching identifier: " +
            $"\"{_missionIdentifier}\". " +
            $"Discovered {newWeapons} new weapon(s) and " +
            $"{newAircraft} new aircraft.");
    }
    
    private static void ReloadOrRecreateConfig(ConfigFile config)
    {
        if (File.Exists(config.ConfigFilePath))
        {
            config.Reload();
            return;
        }
        
        Plugin.Logger.LogInfo(
            $"Restriction config \"{Path.GetFileName(config.ConfigFilePath)}\" " +
            "was deleted. Regenerating it with default values.");
        
        var saveOnConfigSet = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;
        
        try
        {
            foreach (var entry in config)
                entry.Value.BoxedValue = entry.Value.DefaultValue;
        }
        finally
        {
            config.SaveOnConfigSet = saveOnConfigSet;
        }
        
        config.Save();
    }
}