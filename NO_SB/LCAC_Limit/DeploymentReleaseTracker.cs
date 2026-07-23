namespace NO_SB.LCAC_Limit;

internal sealed class DeploymentReleaseTracker
{
    private readonly object _factionKey;
    private readonly ulong _playerSteamId;
    private readonly Unit _unit;
    private readonly string _vehicleName;
    
    private bool _released;
    
    private DeploymentReleaseTracker(Unit unit, ulong playerSteamId, object factionKey, string vehicleName)
    {
        _unit = unit;
        _playerSteamId = playerSteamId;
        _factionKey = factionKey;
        _vehicleName = vehicleName;
    }
    
    internal static bool TryAttach(Unit? unit, SpawnAttemptState state)
    {
        if (unit == null)
        {
            Plugin.Logger.LogError("Cannot attach LCAC deployment tracking: the spawned Unit is null.");
            
            return false;
        }
        
        if (!state.Allowed || !state.Tracked) return false;
        
        if (state.PlayerSteamId == 0UL)
        {
            Plugin.Logger.LogError(
                $"Cannot attach deployment tracking to {state.VehicleName}: its SteamID is missing.");
            
            return false;
        }
        
        var factionKey = state.FactionKey;
        
        if (factionKey == null)
        {
            Plugin.Logger.LogError(
                $"Cannot attach deployment tracking to {state.VehicleName}: its faction key is missing.");
            
            return false;
        }
        
        if (string.IsNullOrWhiteSpace(state.VehicleName))
        {
            Plugin.Logger.LogError("Cannot attach LCAC deployment tracking: the vehicle name is empty.");
            
            return false;
        }
        
        var tracker = new DeploymentReleaseTracker(unit, state.PlayerSteamId, factionKey, state.VehicleName);
        
        unit.onDisableUnit += tracker.OnUnitDisabled;
        
        Plugin.Logger.LogDebug(
            $"Attached deployment release tracking to {state.VehicleName} for SteamID {state.PlayerSteamId}.");
        
        return true;
    }
    
    private void OnUnitDisabled(Unit disabledUnit)
    {
        if (_released)
            return;
        
        _released = true;
        
        _unit.onDisableUnit -= OnUnitDisabled;
        
        LimitDeployment.ReleaseTrackedVehicle(_playerSteamId, _factionKey, _vehicleName);
    }
}