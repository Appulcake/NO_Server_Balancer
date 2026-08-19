using System;
using System.Collections.Generic;

namespace NO_SB.MissionRestrictions;

internal static class AircraftRestrictions
{
    private static readonly Dictionary<string, AircraftEntry> Aircraft = new(StringComparer.Ordinal);
    
    internal static int RefreshConfig()
    {
        if (Encyclopedia.i?.aircraft == null)
            return 0;
        
        var newlyDiscovered = 0;
        
        foreach (var definition in Encyclopedia.i.aircraft)
        {
            if (!definition)
                continue;
            
            var aircraftId = definition.jsonKey.Trim();
            
            if (string.IsNullOrWhiteSpace(aircraftId))
            {
                Plugin.Logger.LogWarning(
                    $"Ignoring AircraftDefinition \"{definition.name}\" for mission restrictions because it has no jsonKey.");
                
                continue;
            }
            
            if (Aircraft.ContainsKey(aircraftId))
                continue;
            
            var displayName = !string.IsNullOrWhiteSpace(definition.unitName)
                ? definition.unitName.Trim()
                : definition.name;
            
            var restriction = new MissionRestrictionEntry(Plugin.AircraftRestrictionsConfig, aircraftId,
                $"{displayName} (Vanilla aircraft restriction identifier: {aircraftId})");
            
            Aircraft.Add(aircraftId, new AircraftEntry
            {
                RestrictionId = aircraftId,
                Restriction = restriction
            });
            
            newlyDiscovered++;
        }
        
        if (newlyDiscovered > 0)
            Plugin.AircraftRestrictionsConfig.Save();
        
        return newlyDiscovered;
    }
    
    internal static IEnumerable<string> GetRestrictedAircraftIds(string missionIdentifier, string factionName)
    {
        foreach (var aircraft in Aircraft.Values)
        {
            if (aircraft.Restriction.ShouldRestrict(missionIdentifier, factionName))
            {
                yield return aircraft.RestrictionId;
            }
        }
    }
    
    private sealed class AircraftEntry
    {
        internal MissionRestrictionEntry Restriction = null!;
        internal string RestrictionId = string.Empty;
    }
}