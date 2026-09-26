using System;
using NuclearOption.SavedMission;

namespace NO_SB.MissionRestrictions;

internal static class PlayerFactionOverrides
{
    private static readonly Random Rng = new();
    private static Mission? _preparedMission;
    private static string _missionIdentifier = string.Empty;
    private static PlayerFactionConfig.PlayerFactionMode? _resolvedMode;
    
    internal static bool HasActiveOverrideForCurrentMission
    {
        get
        {
            var currentMission = MissionManager.CurrentMission;
            return currentMission != null && ReferenceEquals(_preparedMission, currentMission) && _resolvedMode.HasValue;
        }
    }
    
    internal static void Apply(FactionHQ hq, Mission mission)
    {
        PrepareMission(mission);
        
        if (!_resolvedMode.HasValue)
            return;
        
        var factionName = hq.faction.factionName;
        bool preventJoin;
        
        if (string.Equals(factionName, "Primeva", StringComparison.OrdinalIgnoreCase))
            preventJoin = _resolvedMode.Value == PlayerFactionConfig.PlayerFactionMode.Boscali;
        else if (string.Equals(factionName, "Boscali", StringComparison.OrdinalIgnoreCase))
            preventJoin = _resolvedMode.Value == PlayerFactionConfig.PlayerFactionMode.Primeva;
        else
            return;
        
        hq.NetworkpreventJoin = preventJoin;
    }
    
    private static void PrepareMission(Mission mission)
    {
        if (ReferenceEquals(_preparedMission, mission))
            return;
        
        _preparedMission = mission;
        _missionIdentifier = MissionIdentifier.Get(mission);
        _resolvedMode = null;
        
        if (!PlayerFactionConfig.TryRead(out var config))
            return;
        
        PlayerFactionConfig.MissionEntry? matchingEntry = null;
        
        foreach (var entry in config.Missions)
        {
            if (string.IsNullOrWhiteSpace(entry.Mission))
                continue;
            
            if (!string.Equals(entry.Mission.Trim(), _missionIdentifier, StringComparison.OrdinalIgnoreCase))
                continue;
            
            matchingEntry = entry;
            break;
        }
        
        if (matchingEntry == null)
            return;
        
        var configuredMode = matchingEntry.PlayersPlayAs;
        
        _resolvedMode = configuredMode == PlayerFactionConfig.PlayerFactionMode.Random
            ? ResolveRandomFaction()
            : configuredMode;
        
        Plugin.Logger.LogInfo($"Player faction override for \"{_missionIdentifier}\": " +
                              $"{configuredMode}" + (configuredMode == PlayerFactionConfig.PlayerFactionMode.Random
                                  ? $" -> {_resolvedMode.Value}"
                                  : string.Empty));
    }
    
    private static PlayerFactionConfig.PlayerFactionMode
        ResolveRandomFaction() => Rng.Next(2) == 0
        ? PlayerFactionConfig.PlayerFactionMode.Primeva
        : PlayerFactionConfig.PlayerFactionMode.Boscali;
}