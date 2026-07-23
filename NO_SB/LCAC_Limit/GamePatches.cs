using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.DedicatedServer;
using NuclearOption.SavedMission;

namespace NO_SB.LCAC_Limit;

[HarmonyPatch]
internal static class GamePatches
{
    [HarmonyPatch(typeof(DedicatedServerManager), "LoadMissionMap")]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void LoadMissionMapPostfix(Mission mission, ref UniTask<bool> __result)
    {
        __result = AwaitLoadAndClearCounts(mission, __result);
    }
    
    private static async UniTask<bool> AwaitLoadAndClearCounts(Mission mission, UniTask<bool> originalTask)
    {
        var loadedSuccessfully = await originalTask;
        
        if (loadedSuccessfully) LimitDeployment.ClearAllCounts($"mission loaded: {mission.Name}");
        
        return loadedSuccessfully;
    }
}