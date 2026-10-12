using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace NO_SB.Production;

internal static class EnemyAircraftProduction
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _baseMultiplier = null!;
    private static ConfigEntry<float> _additionalPerTenPlayers = null!;
    private static ConfigEntry<float> _maxMultiplier = null!;
    private static ConfigEntry<bool> _stockFloorMode = null!;
    private static ConfigEntry<int> _minimumDeployableStock = null!;
    private static ConfigEntry<float> _floorCheckSeconds = null!;
    private static ConfigEntry<bool> _diagnosticsEnabled = null!;
    private static ConfigEntry<float> _diagnosticsSeconds = null!;
    private static ConfigEntry<bool> _includeOtherStock = null!;
    private static Mission? _trackedMission;
    private static readonly Dictionary<Factory, FactoryState> Factories = new();
    private static readonly Dictionary<AircraftDefinition, int> FactoryCounts = new();
    private static readonly Dictionary<AircraftDefinition, Counters> IntervalCounters = new();
    private static readonly List<Factory> ExpiredFactories = [];
    private static float _nextFloorCheck;
    private static float _nextDiagnostics;
    private static float _nextFactoryPrune;
    
    internal static void Initialise(ConfigFile config)
    {
        const string section = "Enemy Aircraft Production";
        
        _enabled = config.Bind(section, "1. Enabled", false);
        
        _baseMultiplier = config.Bind(section, "2a. Base Multiplier", 1f,
            "Baseline production multiplier (values <1 don't reduce production).");
        _additionalPerTenPlayers = config.Bind(section, "2b. Additional Multiplier Per 10 Players", 1f,
            "Addition to the multiplier for every 10 players in the player faction (fractions also possible).");
        _maxMultiplier = config.Bind(section, "2c. Max Multiplier", 0f,
            "Optional upper limit on the effective multiplier. Set to 0 for no limit.");
        
        _stockFloorMode = config.Bind(section, "3a. Use Minimum Stock Instead", false,
            "Use minimum stock system instead of production boosting (mutually exclusive, when this is " +
            "enabled section 2 is ignored).");
        _minimumDeployableStock = config.Bind(section, "3b. Minimum Deployable Stock", 5,
            "When using minimum stock, keep this many planes above reserve threshold, per aircraft type " +
            "produced. Set 0 to disable.");
        _floorCheckSeconds = config.Bind(section, "3c. Minimum Stock Check Seconds", 15f,
            "How often to replenish aircraft stock, minimum interval of 5 seconds.");
        
        _diagnosticsEnabled = config.Bind(section, "9a. Diagnostics Enabled", false,
            "Periodically log player count, mode, factory output and the aircraft stock.");
        _diagnosticsSeconds = config.Bind(section, "9b. Diagnostics Seconds", 60f,
            "Diagnostics reporting interval (minimum interval of 5 seconds).");
        _includeOtherStock = config.Bind(section, "9c. Include Other Stock In Diagnostics", false,
            "Also report the inventory of aircraft not produced by tracked factories (including stock " +
            "added by other mods). These are never boosted by this module.");
    }
    
    internal static void Clear()
    {
        Factories.Clear();
        FactoryCounts.Clear();
        IntervalCounters.Clear();
        ExpiredFactories.Clear();
        _trackedMission = null;
        _nextFloorCheck = 0f;
        _nextDiagnostics = 0f;
        _nextFactoryPrune = 0f;
    }
    
    private static void EstablishMission()
    {
        var mission = MissionManager.CurrentMission;
        if (ReferenceEquals(_trackedMission, mission))
            return;
        
        Clear();
        _trackedMission = mission;
        _nextFloorCheck = Time.realtimeSinceStartup + Math.Max(5f, _floorCheckSeconds.Value);
        _nextDiagnostics = Time.realtimeSinceStartup + Math.Max(5f, _diagnosticsSeconds.Value);
        _nextFactoryPrune = Time.realtimeSinceStartup + 60f;
    }
    
    // Factory.SetFactory()
    internal static void OnFactoryConfigured(Factory factory, string productionType)
    {
        if (factory == null || !IsServerActive())
            return;
        
        EstablishMission();
        if (factory.ProductionUnit is AircraftDefinition aircraft && !string.IsNullOrEmpty(productionType) &&
            Encyclopedia.Lookup.TryGetValue(productionType, out var resolved) && ReferenceEquals(aircraft, resolved))
            TrackFactory(factory, aircraft);
        else
            Factories.Remove(factory);
    }
    
    private static void TrackFactory(Factory factory, AircraftDefinition aircraft)
    {
        if (!Factories.TryGetValue(factory, out var state))
        {
            Factories.Add(factory, new FactoryState { Aircraft = aircraft });
        }
        else if (!ReferenceEquals(state.Aircraft, aircraft))
        {
            state.Aircraft = aircraft;
            state.FractionalBonus = 0d;
        }
    }
    
    // Factory.ProduceUnit()
    internal static void OnProducedUnit(Factory factory)
    {
        if (!_enabled.Value || !MissionManager.IsRunning || !IsServerActive())
            return;
        
        var unit = factory.attachedUnit;
        var aircraft = factory.ProductionUnit as AircraftDefinition;
        var hq = unit != null ? unit.NetworkHQ : null;
        if (aircraft == null || hq == null)
            return;
        
        EstablishMission();
        TrackFactory(factory, aircraft);
        
        if (!TryGetSides(out var playerHq, out var enemyHq) || !ReferenceEquals(hq, enemyHq))
            return;
        
        GetCounters(aircraft).Native++;
        if (_stockFloorMode.Value)
            return;
        
        if (!Factories.TryGetValue(factory, out var state))
            return;
        
        if (!ReferenceEquals(state.Aircraft, aircraft))
        {
            state.Aircraft = aircraft;
            state.FractionalBonus = 0d;
        }
        
        var multiplier = GetMultiplier(playerHq.factionPlayers.Count);
        var accrued = state.FractionalBonus + multiplier - 1d;
        var whole = Math.Floor(accrued);
        state.FractionalBonus = accrued - whole;
        if (whole < 1d)
            return;
        
        var available = int.MaxValue - Math.Max(0L, hq.GetUnitSupply(aircraft));
        if (available <= 0L)
            return;
        
        var bonus = (int)Math.Min(whole, available);
        if (bonus <= 0)
            return;
        
        // Equalizer mod observes this call to apply its modded aircraft production ratios
        hq.AddSupplyUnit(aircraft, bonus);
        GetCounters(aircraft).Bonus += bonus;
    }
    
    private static Counters GetCounters(AircraftDefinition definition)
    {
        if (!IntervalCounters.TryGetValue(definition, out var counters))
            IntervalCounters.Add(definition, counters = new Counters());
        return counters;
    }
    
    private static double GetMultiplier(int players)
    {
        var effective = _baseMultiplier.Value + players * (double)_additionalPerTenPlayers.Value / 10d;
        if (double.IsNaN(effective) || double.IsInfinity(effective))
            return 1d;
        
        if (_maxMultiplier.Value > 0f)
            effective = Math.Min(effective, _maxMultiplier.Value);
        
        return Math.Max(1d, effective);
    }
    
    private static bool TryGetSides(out FactionHQ playerHq, out FactionHQ enemyHq)
    {
        playerHq = null!;
        enemyHq = null!;
        var joinable = 0;
        var blocked = 0;
        foreach (var hq in FactionRegistry.GetAllHQs())
        {
            if (hq == null || hq.faction == null)
                continue;
            
            if (hq.preventJoin)
            {
                blocked++;
                enemyHq = hq;
            }
            else
            {
                joinable++;
                playerHq = hq;
            }
        }
        
        return joinable == 1 && blocked == 1;
    }
    
    private static bool IsServerActive() =>
        NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active;
    
    internal static IEnumerator Monitor()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(5f);
            if (!_enabled.Value || !IsServerActive() || !MissionManager.IsRunning)
                continue;
            
            EstablishMission();
            var now = Time.realtimeSinceStartup;
            if (now >= _nextFactoryPrune)
            {
                _nextFactoryPrune = now + 60f;
                RemoveExpiredFactories();
            }
            
            var floorDue = _stockFloorMode.Value && now >= _nextFloorCheck;
            var diagnosticsDue = _diagnosticsEnabled.Value && now >= _nextDiagnostics;
            switch (floorDue)
            {
                case false when !diagnosticsDue:
                    continue;
                case true:
                    _nextFloorCheck = now + Math.Max(5f, _floorCheckSeconds.Value);
                    break;
            }
            
            if (diagnosticsDue)
                _nextDiagnostics = now + Math.Max(5f, _diagnosticsSeconds.Value);
            
            if (!TryGetSides(out var playerHq, out var enemyHq))
            {
                if (diagnosticsDue)
                {
                    Plugin.Logger.LogInfo("Couldn't determine player vs enemy side, diagnostics skipped: exactly one " +
                                          "joinable and one preventJoin faction are required.");
                    IntervalCounters.Clear();
                }
                
                continue;
            }
            
            CollectFactories(enemyHq);
            if (floorDue)
                ApplyFloor(enemyHq);
            if (diagnosticsDue)
                WriteDiagnostics(playerHq, enemyHq);
        }
        // ReSharper disable once IteratorNeverReturns
    }
    
    private static void RemoveExpiredFactories()
    {
        ExpiredFactories.Clear();
        foreach (var factory in Factories.Keys)
            if (!factory)
                ExpiredFactories.Add(factory);
        foreach (var factory in ExpiredFactories)
            Factories.Remove(factory);
        ExpiredFactories.Clear();
    }
    
    private static void CollectFactories(FactionHQ enemyHq)
    {
        FactoryCounts.Clear();
        RemoveExpiredFactories();
        foreach (var entry in Factories)
        {
            var factory = entry.Key;
            var unit = factory.attachedUnit;
            if (unit == null || unit.disabled || !ReferenceEquals(unit.NetworkHQ, enemyHq) ||
                factory.ProductionUnit is not AircraftDefinition aircraft)
                continue;
            
            if (!FactoryCounts.TryGetValue(aircraft, out var count))
                count = 0;
            FactoryCounts[aircraft] = count + 1;
        }
    }
    
    private static void ApplyFloor(FactionHQ enemyHq)
    {
        if (_minimumDeployableStock.Value <= 0)
            return;
        
        var reserve = Math.Max(0L,
            enemyHq.reserveAirframes + (long)enemyHq.factionPlayers.Count * enemyHq.extraReservesPerPlayer);
        var desired = Math.Min(int.MaxValue, reserve + _minimumDeployableStock.Value);
        foreach (var entry in FactoryCounts)
        {
            var aircraft = entry.Key;
            var current = enemyHq.GetUnitSupply(aircraft);
            var deficit = desired - current;
            if (deficit <= 0)
                continue;
            
            var amount = (int)Math.Min(deficit, int.MaxValue - Math.Max(0L, current));
            if (amount <= 0)
                continue;
            
            enemyHq.AddSupplyUnit(aircraft, amount);
            GetCounters(aircraft).Floor += amount;
        }
    }
    
    private static void WriteDiagnostics(FactionHQ playerHq, FactionHQ enemyHq)
    {
        long native = 0, bonus = 0, floor = 0;
        foreach (var counters in IntervalCounters.Values)
        {
            native += counters.Native;
            bonus += counters.Bonus;
            floor += counters.Floor;
        }
        
        var floorMode = _stockFloorMode.Value;
        var log = new StringBuilder(512);
        log.Append("--- Enemy Aircraft Diagnostics | players: ").Append(playerHq.factionPlayers.Count)
            .Append(" | enemy: ").Append(enemyHq.faction.factionName)
            .Append(" | mode: ").Append(floorMode ? "Floor" : "Boost");
        
        if (floorMode)
            log.Append(" | minAboveReserve: ").Append(_minimumDeployableStock.Value)
                .Append(" | reserve: ").Append(enemyHq.reserveAirframes);
        else
            log.Append(" x").Append(GetMultiplier(playerHq.factionPlayers.Count).ToString("0.##"));
        
        var totalFactories = 0;
        foreach (var number in FactoryCounts.Values)
            totalFactories += number;
        
        log.Append(" | factories: ").Append(totalFactories)
            .Append(" | cycles: ").Append(native)
            .Append(" | bonus: ").Append(bonus)
            .Append(" | floor: ").Append(floor)
            .Append(" | ");
        
        if (FactoryCounts.Count == 0)
            log.Append("(no active enemy aircraft factories)");
        
        var first = true;
        foreach (var entry in FactoryCounts)
        {
            var aircraft = entry.Key;
            IntervalCounters.TryGetValue(aircraft, out var counters);
            if (!first)
                log.Append("; ");
            first = false;
            log.Append(aircraft.jsonKey)
                .Append("=").Append(enemyHq.GetUnitSupply(aircraft))
                .Append(" (fac:").Append(entry.Value)
                .Append(" n:").Append(counters?.Native ?? 0)
                .Append(" +:").Append(counters?.Bonus ?? 0)
                .Append(" f:").Append(counters?.Floor ?? 0).Append(')');
        }
        
        if (_includeOtherStock.Value)
        {
            log.Append(" | other supply (not boosted): ");
            var any = false;
            foreach (var entry in enemyHq.AircraftSupply)
            {
                if (entry.Key == null || FactoryCounts.ContainsKey(entry.Key) || entry.Value.Count <= 0)
                    continue;
                if (any)
                    log.Append(", ");
                any = true;
                log.Append(entry.Key.jsonKey).Append('=').Append(entry.Value.Count);
            }
            
            if (!any)
                log.Append("(none)");
        }
        
        Plugin.Logger.LogInfo(log.ToString());
        IntervalCounters.Clear();
    }
    
    private sealed class FactoryState
    {
        internal AircraftDefinition? Aircraft;
        internal double FractionalBonus;
    }
    
    private sealed class Counters
    {
        internal long Bonus;
        internal long Floor;
        internal long Native;
    }
}

[HarmonyPatch]
internal static class EnemyAircraftProductionPatches
{
    [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.SetMission), typeof(Mission), typeof(bool))]
    [HarmonyPrefix]
    private static void MissionChanged(Mission mission)
    {
        if (!ReferenceEquals(MissionManager.CurrentMission, mission))
            EnemyAircraftProduction.Clear();
    }
    
    [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.StartMission))]
    [HarmonyPrefix]
    private static void MissionStarting() => EnemyAircraftProduction.Clear();
    
    [HarmonyPatch(typeof(Factory), nameof(Factory.SetFactory), typeof(string), typeof(float))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void FactoryConfigured(Factory __instance, string productionType) =>
        EnemyAircraftProduction.OnFactoryConfigured(__instance, productionType);
    
    [HarmonyPatch(typeof(Factory), nameof(Factory.ProduceUnit))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void FactoryProduced(Factory __instance) => EnemyAircraftProduction.OnProducedUnit(__instance);
}