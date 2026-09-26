using HarmonyLib;
// ReSharper disable InconsistentNaming

namespace NO_SB.MissionRestrictions;

internal static class GWCompatibility
{
    private const string MissionBalanceTypeName = "GW_server_plugin.Features.MissionBalanceService";
    
    internal static void TryPatch(Harmony harmony)
    {
        var missionBalanceType = AccessTools.TypeByName(MissionBalanceTypeName);
        if (missionBalanceType == null)
            return;
        
        var target = AccessTools.Method(missionBalanceType, "CheckAndApplyBalance");
        var prefix = AccessTools.Method(typeof(GWCompatibility), nameof(CheckAndApplyBalancePrefix));
        if (target == null || prefix == null)
        {
            Plugin.Logger.LogWarning("GW plugin found, but MissionBalanceService.CheckAndApplyBalance could not be patched.");
            return;
        }
        
        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        Plugin.Logger.LogInfo("GW mission balance compatibility enabled.");
    }
    
    private static bool CheckAndApplyBalancePrefix()
    {
        return !PlayerFactionOverrides.HasActiveOverrideForCurrentMission;
    }
}