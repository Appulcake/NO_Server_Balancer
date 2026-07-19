using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NO_Server_Balancer;

// Test attempting to enable and change turret auto tracking, doesn't apply on servers for now (so e.g. no automated turret on short range LADS)
internal static class SetTurretTracking
{
    private static readonly HashSet<string> TurretNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "kar_turret_laser",
            "kar_turret_laserhp",
            "truk_laser",
            "laser_turret"
        };
    
    public static void ApplyTracking(GameObject parentVehicle, string assetName)
    {
        var isTargetLaserTurretPrefab =
            TurretNames.Contains(parentVehicle.name) ||
            TurretNames.Any(turretName =>
                assetName.EndsWith(
                    $"/{turretName}.prefab", StringComparison.OrdinalIgnoreCase));
        
        if (!isTargetLaserTurretPrefab)
            return;
        
        // Plugin.Logger.LogInfo($"[ServerBalancer] SetTurretTracking Turret: Found {parentVehicle.name}");
        
        var turrets = parentVehicle.GetComponentsInChildren<Turret>(true);
        
        var seenTurrets = new HashSet<int>();
        var seenLasers = new HashSet<int>();
        
        foreach (var turret in turrets)
        {
            if (!turret)
                continue;
            
            if (!seenTurrets.Add(turret.GetInstanceID()))
                continue;
            
            if (!TurretNames.Contains(turret.gameObject.name))
                continue;
            
            // Plugin.Logger.LogInfo($"[ServerBalancer] SetTurretTracking Found {parentVehicle.name}'s {turret.gameObject.name}");
            
            turret.targetAssessmentInterval = 0.1f;
            turret.aimSolver.simulationInterval = 0.1f;
            turret.lockTime = 0.01f;
            turret.maxElevation = 90f;
            turret.aimSolver.rakeAmount = 0f;
            turret.aimSolver.rakeFrequency = 0f;
            
            switch (turret.gameObject.name)
            {
                case "kar_turret_laser":
                {
                    var laser = turret.GetComponentInChildren<Laser>(true);
                    
                    if (!laser)
                        continue;
                    
                    if (!seenLasers.Add(laser.GetInstanceID()))
                        continue;
                    
                    if (!string.Equals(laser.gameObject.name, "laser_barrel", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    // Plugin.Logger.LogInfo($"[ServerBalancer] SetTurretTracking Found {laser.gameObject.name}");
                    
                    laser.info.targetRequirements.maxRange = 50000f;
                    laser.info.targetRequirements.maxSpeed = 100000f;
                    laser.info.targetRequirements.minAlignment = 30f;
                    
                    LASS.SavedModifiedAssets.Add(laser);
                    break;
                }
                case "kar_turret_laserhp":
                {
                    var laser = turret.GetComponentInChildren<Laser>(true);
                    
                    if (!laser)
                        continue;
                    
                    if (!seenLasers.Add(laser.GetInstanceID()))
                        continue;
                    
                    if (!string.Equals(laser.gameObject.name, "laser_barrel", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    // Plugin.Logger.LogInfo($"[ServerBalancer] SetTurretTracking Found {laser.gameObject.name}");
                    
                    laser.info.targetRequirements.maxRange = 25000f;
                    
                    LASS.SavedModifiedAssets.Add(laser);
                    break;
                }
                case "laser_turret":
                {
                    var laser = turret.GetComponentInChildren<Laser>(true);
                    
                    if (!laser)
                        continue;
                    
                    if (!seenLasers.Add(laser.GetInstanceID()))
                        continue;
                    
                    if (!string.Equals(laser.gameObject.name, "laser_barrel", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Plugin.Logger.LogInfo($"[ServerBalancer] SetTurretTracking Found {laser.gameObject.name}");
                    
                    laser.info.targetRequirements.maxRange = 25000f;
                    
                    LASS.SavedModifiedAssets.Add(laser);
                    break;
                }
            }
            
            LASS.SavedModifiedAssets.Add(parentVehicle);
            LASS.SavedModifiedAssets.Add(turret);
        }
    }
}