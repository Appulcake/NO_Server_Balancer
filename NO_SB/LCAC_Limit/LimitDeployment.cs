using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NO_SB.LCAC_Limit;

internal enum SpawnDenialReason
{
    None,
    PlayerLimit,
    FactionLimit,
    MissingOwnerInformation
}

// Rider code clean-up keeps re-ordering this, I'm done keeping it neat
internal sealed class SpawnAttemptState
{
    internal bool Allowed;
    
    internal SpawnDenialReason DenialReason;
    
    internal int FactionCountAfter;
    internal int FactionCountBefore;
    
    internal object? FactionKey;
    internal bool Finalised;
    
    internal VehicleLimit? Limit;
    
    internal int PlayerCountAfter;
    internal int PlayerCountBefore;
    
    internal ulong PlayerSteamId;
    
    internal bool Reserved;
    internal bool Tracked;
    
    internal string VehicleName = string.Empty;
}

internal static class LimitDeployment
{
    private static readonly object SyncRoot = new();
    
    private static readonly Dictionary<ulong, Dictionary<string, int>> PlayerVehicleCounts = new();
    
    private static readonly Dictionary<object, Dictionary<string, int>> FactionVehicleCounts =
        new(ReferenceEqualityComparer.Instance);
    
    private static VehicleLimitConfig? _config;
    
    private static VehicleLimitConfig Config => _config ??
                                                throw new InvalidOperationException(
                                                    $"{nameof(LimitDeployment)} has not been initialised.");
    
    internal static void Initialise(VehicleLimitConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }
    
    internal static SpawnAttemptState CheckAndReserve(string vehicleName, ulong playerSteamId, object? factionKey)
    {
        var state = new SpawnAttemptState
        {
            VehicleName = vehicleName,
            PlayerSteamId = playerSteamId,
            FactionKey = factionKey
        };
        
        if (!Config.TryGetLimit(vehicleName, out var limit))
        {
            state.Allowed = Config.AllowUnconfiguredVehicles;
            state.Tracked = false;
            
            if (state.Allowed)
                Plugin.Logger.LogWarning(
                    $"Deployable vehicle {vehicleName} has no configured limit. Allowing it without applying any.");
            else
                Plugin.Logger.LogWarning($"Deployable vehicle {vehicleName} has no configured limit and was denied.");
            
            
            return state;
        }
        
        state.Limit = limit;
        state.Tracked = true;
        
        if (playerSteamId == 0UL || factionKey == null)
        {
            state.Allowed = false;
            state.DenialReason = SpawnDenialReason.MissingOwnerInformation;
            return state;
        }
        
        lock (SyncRoot)
        {
            var playerCount = GetCount(PlayerVehicleCounts, playerSteamId, vehicleName);
            
            var factionCount = GetCount(FactionVehicleCounts, factionKey, vehicleName);
            
            state.PlayerCountBefore = playerCount;
            state.PlayerCountAfter = playerCount;
            
            state.FactionCountBefore = factionCount;
            state.FactionCountAfter = factionCount;
            
            if (limit.PlayerLimit >= 0 && playerCount >= limit.PlayerLimit)
            {
                state.Allowed = false;
                state.DenialReason = SpawnDenialReason.PlayerLimit;
                return state;
            }
            
            if (limit.FactionLimit >= 0 && factionCount >= limit.FactionLimit)
            {
                state.Allowed = false;
                state.DenialReason = SpawnDenialReason.FactionLimit;
                return state;
            }
            
            state.PlayerCountAfter = IncrementCount(PlayerVehicleCounts, playerSteamId, vehicleName);
            
            state.FactionCountAfter = IncrementCount(FactionVehicleCounts, factionKey, vehicleName);
            
            state.Allowed = true;
            state.Reserved = true;
            
            return state;
        }
    }
    
    internal static void FinaliseAttempt(SpawnAttemptState? state, bool spawnSucceeded)
    {
        if (state == null) return;
        
        lock (SyncRoot)
        {
            if (state.Finalised)
                return;
            
            state.Finalised = true;
            
            if (!state.Reserved)
                return;
            
            if (spawnSucceeded)
            {
                state.Reserved = false;
                return;
            }
            
            state.PlayerCountAfter = DecrementCount(PlayerVehicleCounts, state.PlayerSteamId, state.VehicleName);
            
            var factionKey = state.FactionKey;
            
            if (factionKey != null)
                state.FactionCountAfter = DecrementCount(FactionVehicleCounts, factionKey, state.VehicleName);
            else
                Plugin.Logger.LogError(
                    $"Cannot roll back deployment reservation for {state.VehicleName}: the reserved attempt has no faction key.");
            
            state.Reserved = false;
        }
    }
    
    internal static void ClearAllCounts(string reason)
    {
        lock (SyncRoot)
        {
            PlayerVehicleCounts.Clear();
            FactionVehicleCounts.Clear();
        }
        
        Plugin.Logger.LogInfo($"Cleared all deployed vehicle counts. Reason: {reason}");
    }
    
    internal static string BuildDeniedMessage(SpawnAttemptState state)
    {
        switch (state.DenialReason)
        {
            case SpawnDenialReason.PlayerLimit:
            {
                var limit = RequireLimit(state);
                
                return
                    $"Cannot spawn {state.VehicleName}: " +
                    $"player limit reached " +
                    $"({FormatUsage(state.PlayerCountBefore, limit.PlayerLimit)}). " +
                    $"Faction usage is " +
                    $"{FormatUsage(state.FactionCountBefore, limit.FactionLimit)}.";
            }
            
            case SpawnDenialReason.FactionLimit:
            {
                var limit = RequireLimit(state);
                
                return
                    $"Cannot spawn {state.VehicleName}: " +
                    $"faction limit reached " +
                    $"({FormatUsage(state.FactionCountBefore, limit.FactionLimit)}). " +
                    $"Player usage is " +
                    $"{FormatUsage(state.PlayerCountBefore, limit.PlayerLimit)}.";
            }
            
            case SpawnDenialReason.MissingOwnerInformation:
                return
                    $"Cannot spawn {state.VehicleName} because its " +
                    "player or faction owner could not be determined.";
            
            default:
                return
                    $"Cannot spawn {state.VehicleName}.";
        }
    }
    
    private static VehicleLimit RequireLimit(SpawnAttemptState state) => state.Limit ??
                                                                         throw new InvalidOperationException(
                                                                             $"Deployment attempt for '{state.VehicleName}' requires a configured limit, but none was stored.");
    
    internal static string BuildSpawnedMessage(SpawnAttemptState state)
    {
        if (!state.Tracked) return $"Spawned {state.VehicleName} without a configured limit.";
        
        var limit = RequireLimit(state);
        
        return
            $"Spawned {state.VehicleName}: " +
            $"player usage " +
            $"{FormatUsage(state.PlayerCountAfter, limit.PlayerLimit)}; " +
            $"faction usage " +
            $"{FormatUsage(state.FactionCountAfter, limit.FactionLimit)}.";
    }
    
    private static string FormatUsage(int current, int limit) =>
        limit < 0 ? $"{current}/unlimited" : $"{current}/{limit}";
    
    private static int GetCount<TKey>(Dictionary<TKey, Dictionary<string, int>> ownerCounts, TKey ownerKey,
        string vehicleName)
        where TKey : notnull
    {
        if (!ownerCounts.TryGetValue(ownerKey, out var vehicleCounts))
            return 0;
        
        return vehicleCounts.TryGetValue(vehicleName, out var count) ? count : 0;
    }
    
    private static int IncrementCount<TKey>(Dictionary<TKey, Dictionary<string, int>> ownerCounts, TKey ownerKey,
        string vehicleName)
        where TKey : notnull
    {
        if (!ownerCounts.TryGetValue(ownerKey, out var vehicleCounts))
        {
            vehicleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            
            ownerCounts.Add(ownerKey, vehicleCounts);
        }
        
        var nextCount = vehicleCounts.TryGetValue(vehicleName, out var count) ? count + 1 : 1;
        
        vehicleCounts[vehicleName] = nextCount;
        return nextCount;
    }
    
    private static int DecrementCount<TKey>(Dictionary<TKey, Dictionary<string, int>> ownerCounts, TKey ownerKey,
        string vehicleName)
        where TKey : notnull
    {
        if (!ownerCounts.TryGetValue(ownerKey, out var vehicleCounts) ||
            !vehicleCounts.TryGetValue(vehicleName, out var count))
            return 0;
        
        var nextCount = Math.Max(0, count - 1);
        
        if (nextCount > 0)
        {
            vehicleCounts[vehicleName] = nextCount;
            return nextCount;
        }
        
        vehicleCounts.Remove(vehicleName);
        
        if (vehicleCounts.Count == 0)
            ownerCounts.Remove(ownerKey);
        
        return 0;
    }
    
    internal static void ReleaseTrackedVehicle(ulong playerSteamId, object factionKey, string vehicleName)
    {
        int playerCountBefore;
        int playerCountAfter;
        
        int factionCountBefore;
        int factionCountAfter;
        
        lock (SyncRoot)
        {
            playerCountBefore = GetCount(PlayerVehicleCounts, playerSteamId, vehicleName);
            
            factionCountBefore = GetCount(FactionVehicleCounts, factionKey, vehicleName);
            
            if (playerCountBefore == 0 && factionCountBefore == 0)
                return;
            
            playerCountAfter = DecrementCount(PlayerVehicleCounts, playerSteamId, vehicleName);
            
            factionCountAfter = DecrementCount(FactionVehicleCounts, factionKey, vehicleName);
        }
        
        if (playerCountBefore == 0 || factionCountBefore == 0)
        {
            Plugin.Logger.LogWarning(
                $"Released tracked vehicle {vehicleName}, but its counters were inconsistent. Player:" +
                $" {playerCountBefore} => {playerCountAfter}, faction: {factionCountBefore} => {factionCountAfter}.");
            
            return;
        }
        
        Plugin.Logger.LogInfo(
            $"Removed disabled vehicle {vehicleName} from active limits: player {playerCountBefore} =>" +
            $" {playerCountAfter}, faction {factionCountBefore} => {factionCountAfter}.");
    }
    
    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static readonly IEqualityComparer<object> Instance = new ReferenceEqualityComparer();
        
        bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);
        
        int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}