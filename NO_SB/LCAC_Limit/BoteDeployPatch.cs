using System;
using HarmonyLib;
using NOComponentWIP;

// ReSharper disable InconsistentNaming

namespace NO_SB.LCAC_Limit;

[HarmonyPatch]
internal static class BoteDeployPatch
{
    [HarmonyPatch(typeof(DeployableVehicle), nameof(DeployableVehicle.SpawnUnit))]
    [HarmonyPrefix]
    private static bool Prefix(DeployableVehicle __instance, Aircraft aircraft, ref bool spawned, ref Unit? __result,
        out SpawnAttemptState? __state)
    {
        __state = null;
        
        var vehicleName = GetVehicleName(__instance);
        
        if (string.IsNullOrWhiteSpace(vehicleName))
        {
            Plugin.Logger.LogWarning("Could not determine the unitName for a deployable vehicle. No limits applied!");
            
            return true;
        }
        
        var player = aircraft.Player;
        
        if (!Utils.TryGetPlayerSteamId(player, out var playerSteamId))
        {
            spawned = false;
            __result = null;
            
            const string message = "Cannot deploy vehicle: your SteamID could not be determined.";
            
            Plugin.Logger.LogWarning($"{message} Player: {player.PlayerName}");
            
            Utils.TrySendWhisper(player, message);
            
            return false;
        }
        
        var attempt = LimitDeployment.CheckAndReserve(vehicleName, playerSteamId, aircraft.NetworkHQ);
        
        __state = attempt;
        
        if (attempt.Allowed)
            return true;
        
        spawned = false;
        __result = null;
        
        
        var denialMessage = LimitDeployment.BuildDeniedMessage(attempt);
        
        Plugin.Logger.LogWarning(denialMessage);
        
        Utils.TrySendWhisper(player, denialMessage);
        
        return false;
    }
    
    [HarmonyPatch(typeof(DeployableVehicle), nameof(DeployableVehicle.SpawnUnit))]
    [HarmonyPostfix]
    private static void Postfix(Aircraft aircraft, bool spawned, Unit? __result, SpawnAttemptState? __state)
    {
        if (__state is not { Allowed: true })
            return;
        
        var spawnedUnit = __result;
        var spawnSucceeded = spawned && spawnedUnit != null;
        
        LimitDeployment.FinaliseAttempt(__state, spawnSucceeded);
        
        if (spawnSucceeded)
        {
            DeploymentReleaseTracker.TryAttach(spawnedUnit, __state);
            
            var message = LimitDeployment.BuildSpawnedMessage(__state);
            
            Plugin.Logger.LogInfo(message);
            
            Utils.TrySendWhisper(aircraft.Player, message);
            
            return;
        }
        
        Plugin.Logger.LogWarning(
            $"The spawn call for {__state.VehicleName} was not exceeding set limits, " +
            "but the original BOTE spawn method failed, and thus its reservation was rolled back.");
    }
    
    [HarmonyPatch(typeof(DeployableVehicle), nameof(DeployableVehicle.SpawnUnit))]
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, SpawnAttemptState? __state)
    {
        if (__exception == null || __state == null) return __exception;
        
        LimitDeployment.FinaliseAttempt(__state, false);
        
        Plugin.Logger.LogError(
            $"BOTE failed while spawning {__state.VehicleName}, and thus its reservation was rolled back.");
        
        return __exception;
    }
    
    private static string GetVehicleName(DeployableVehicle deployableVehicle)
    {
        var unitName = deployableVehicle.unitName;
        
        return string.IsNullOrWhiteSpace(unitName) ? string.Empty : unitName.Trim();
    }
}