using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace NO_Server_Balancer;

internal static class Utils
{
    // ReSharper disable All
    public static readonly Dictionary<string, AnimationCurve> DamageCurves = new()
    {
        ["First_LADS"] = CreateLinearCurve(
            new Keyframe(0f, 1f),
            new Keyframe(15000f, 1f),
            new Keyframe(50000f, 0f)),
        ["New_70kW"] = CreateLinearCurve(
            new Keyframe(0f, 1f),
            new Keyframe(10000f, 1f),
            new Keyframe(20000f, 0.5f),
            new Keyframe(25000f, 0f)),
        ["New_LADS"] = CreateLinearCurve(
            new Keyframe(0f, 1f),
            new Keyframe(5000f, 0.9f),
            new Keyframe(7500f, 0.8f),
            new Keyframe(10000f, 0.5f),
            new Keyframe(12500f, 0.1f),
            new Keyframe(15000f, 0.05f),
            new Keyframe(25000f, 0f)),
        ["Original_Curve"] = new AnimationCurve(
            new Keyframe(0f, 1f, 0.000015f, 0.000015f, 0f, 0.0767408f),
            new Keyframe(20000f, 0f, -0.0000086f, -0.0000086f, 0.074349314f, 0f))
    };
    // ReSharper restore All
    
    private static AnimationCurve CreateLinearCurve(params Keyframe[] keys)
    {
        var curve = new AnimationCurve(keys);
        
        for (var i = 0; i < keys.Length; i++)
        {
            if (i > 0)
            {
                var incomingSlope = (keys[i].value - keys[i - 1].value) / (keys[i].time - keys[i - 1].time);
                
                AnimationUtilityReplacement.SetIncomingTangent(curve, i, incomingSlope);
            }
            
            if (i < keys.Length - 1)
            {
                var outgoingSlope = (keys[i + 1].value - keys[i].value) / (keys[i + 1].time - keys[i].time);
                
                AnimationUtilityReplacement.SetOutgoingTangent(curve, i, outgoingSlope);
            }
        }
        
        return curve;
    }
    
    public static void ExportAnimationCsv(string outputPath, IReadOnlyDictionary<string, AnimationCurve> curves,
        float startTime,
        float endTime, int sampleCount = 201)
    {
        if (sampleCount < 2)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        
        using var writer = new StreamWriter(outputPath);
        
        writer.Write("Time");
        
        foreach (var curveName in curves.Keys)
            writer.Write($",{curveName}");
        
        writer.WriteLine();
        
        for (var i = 0; i < sampleCount; i++)
        {
            var t = Mathf.Lerp(startTime, endTime, i / (sampleCount - 1f));
            
            writer.Write(t.ToString(CultureInfo.InvariantCulture));
            
            foreach (var curve in curves.Values)
            {
                var value = curve.Evaluate(t);
                
                writer.Write(",");
                writer.Write(value.ToString(CultureInfo.InvariantCulture));
            }
            
            writer.WriteLine();
        }
    }
    
    public static bool HasUnityName(string? actualName, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(actualName))
            return false;
        
        var normalizedName = actualName?.Replace("(Clone)", string.Empty).Trim();
        
        return string.Equals(normalizedName, expectedName, StringComparison.OrdinalIgnoreCase);
    }
    
    public static Laser? FindMountedLaser(Transform trukRoot, string turretName)
    {
        var descendants = trukRoot.GetComponentsInChildren<Transform>(true);
        
        foreach (var descendant in descendants)
        {
            if (!descendant)
                continue;
            
            if (!HasUnityName(descendant.name, turretName)) continue;
            
            var lasers = descendant.GetComponentsInChildren<Laser>(true);
            
            foreach (var laser in lasers)
            {
                if (!laser)
                    continue;
                
                if (!HasUnityName(laser.gameObject.name, "laser_barrel")) continue;
                
                return laser;
            }
        }
        
        return null;
    }
    
    private static class AnimationUtilityReplacement
    {
        public static void SetIncomingTangent(AnimationCurve curve, int index, float tangent)
        {
            var key = curve[index];
            key.inTangent = tangent;
            curve.MoveKey(index, key);
        }
        
        public static void SetOutgoingTangent(AnimationCurve curve, int index, float tangent)
        {
            var key = curve[index];
            key.outTangent = tangent;
            curve.MoveKey(index, key);
        }
    }
}