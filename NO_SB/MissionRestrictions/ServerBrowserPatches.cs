using HarmonyLib;
using NuclearOption.Networking.Lobbies;
using NuclearOption.SavedMission;

namespace NO_SB.MissionRestrictions;

[HarmonyPatch]
internal static class ServerBrowserPatches
{
    [HarmonyPatch(typeof(MissionTag), nameof(MissionTag.GetPvpTypeLobbyString), [typeof(Mission)])]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void ForceDedicatedServerPvE(ref string __result)
    {
        if (!GameManager.IsHeadless || !Plugin.MarkServerAsAlwaysPvE.Value)
            return;
        
        __result = MissionTag.GetPvpTypeLobbyString(MissionPvpType.Pve);
    }
}