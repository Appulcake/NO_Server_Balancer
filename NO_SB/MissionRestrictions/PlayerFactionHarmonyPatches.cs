using HarmonyLib;
using NuclearOption.SavedMission;

namespace NO_SB.MissionRestrictions;

[HarmonyPatch]
internal static class PlayerFactionHarmonyPatches
{
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.OnMissionLoad))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void FactionHQOnMissionLoadPostfix(FactionHQ __instance, Mission? mission)
    {
        if (!__instance.IsServer || mission == null)
            return;
        
        PlayerFactionOverrides.Apply(__instance, mission);
    }
}