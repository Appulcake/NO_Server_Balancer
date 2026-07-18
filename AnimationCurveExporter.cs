using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace NO_Server_Balancer;

internal static class AnimationCurveExporter
{
    public static void ExportCsv(string outputPath, IReadOnlyDictionary<string, AnimationCurve> curves, float startTime,
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
}