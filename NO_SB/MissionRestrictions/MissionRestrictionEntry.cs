using System;
using BepInEx.Configuration;

namespace NO_SB.MissionRestrictions;

internal sealed class MissionRestrictionEntry
{
    private readonly ConfigEntry<bool> _restricted;

    private readonly ConfigEntry<bool> _applyToPrimeva;
    private readonly ConfigEntry<string> _primevaMissionExceptions;

    private readonly ConfigEntry<bool> _applyToBoscali;
    private readonly ConfigEntry<string> _boscaliMissionExceptions;

    internal MissionRestrictionEntry(ConfigFile config, string section, string description)
    {
        _restricted = config.Bind(section, "Restricted", false, new ConfigDescription(description));

        _applyToPrimeva = config.Bind(section, "ApplyToPrimeva", true,
            new ConfigDescription("Apply restriction to Primeva."));

        _primevaMissionExceptions = config.Bind(section, "PrimevaMissionExceptions", string.Empty,
            new ConfigDescription("Semicolon separated missions where Restricted is inverted for Primeva."));

        _applyToBoscali = config.Bind(section, "ApplyToBoscali", true,
            new ConfigDescription("Apply restriction to Boscali."));

        _boscaliMissionExceptions = config.Bind(section, "BoscaliMissionExceptions", string.Empty,
            new ConfigDescription("Semicolon separated missions where Restricted is inverted for Boscali."));
    }

    internal bool ShouldRestrict(string missionIdentifier, string factionName)
    {
        if (string.Equals(factionName, "Primeva", StringComparison.OrdinalIgnoreCase))
        {
            return ShouldRestrictForFaction(_applyToPrimeva.Value, _primevaMissionExceptions.Value, missionIdentifier);
        }

        if (string.Equals(factionName, "Boscali", StringComparison.OrdinalIgnoreCase))
        {
            return ShouldRestrictForFaction(_applyToBoscali.Value, _boscaliMissionExceptions.Value, missionIdentifier);
        }
        
        return _restricted.Value;
    }

    private bool ShouldRestrictForFaction(bool appliesToFaction, string missionExceptions, string missionIdentifier)
    {
        if (!appliesToFaction)
            return false;

        var missionIsException = ContainsMission(missionExceptions, missionIdentifier);

        return _restricted.Value ^ missionIsException;
    }

    private static bool ContainsMission(string configuredMissions, string missionIdentifier)
    {
        if (string.IsNullOrWhiteSpace(configuredMissions) || string.IsNullOrWhiteSpace(missionIdentifier))
        {
            return false;
        }

        var missions = configuredMissions.Split([';'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var configuredMission in missions)
        {
            if (string.Equals(configuredMission.Trim(), missionIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}