using System;
using System.Collections.Generic;

namespace NO_SB.MissionRestrictions;

internal static class WeaponRestrictions
{
    private static readonly Dictionary<string, WeaponEntry> Weapons = new(StringComparer.Ordinal);
    
    internal static int RefreshConfig()
    {
        if (Encyclopedia.i?.weaponMounts == null)
            return 0;
        
        var newlyDiscovered = 0;
        
        foreach (var mount in Encyclopedia.i.weaponMounts)
        {
            if (!mount)
                continue;
            
            var runtimeName = mount.name.Trim();
            
            if (string.IsNullOrWhiteSpace(runtimeName))
                continue;
            
            if (Weapons.ContainsKey(runtimeName))
                continue;
            
            var configId = !string.IsNullOrWhiteSpace(mount.jsonKey)
                ? mount.jsonKey.Trim()
                : runtimeName;
            
            var displayName = !string.IsNullOrWhiteSpace(mount.mountName)
                ? mount.mountName.Trim()
                : runtimeName;
            
            var restriction = new MissionRestrictionEntry(Plugin.WeaponRestrictionsConfig, configId,
                $"{displayName} (Config identifier: {configId}, vanilla runtime restriction name: {runtimeName})");
            
            Weapons.Add(runtimeName, new WeaponEntry
            {
                RuntimeRestrictionName = runtimeName,
                Restriction = restriction
            });
            
            newlyDiscovered++;
        }
        
        if (newlyDiscovered > 0)
            Plugin.WeaponRestrictionsConfig.Save();
        
        return newlyDiscovered;
    }
    
    internal static IEnumerable<string> GetRestrictedWeaponNames(string missionIdentifier, string factionName)
    {
        foreach (var weapon in Weapons.Values)
        {
            if (weapon.Restriction.ShouldRestrict(missionIdentifier, factionName))
            {
                yield return weapon.RuntimeRestrictionName;
            }
        }
    }
    
    private sealed class WeaponEntry
    {
        internal MissionRestrictionEntry Restriction = null!;
        internal string RuntimeRestrictionName = string.Empty;
    }
}