using System;
using System.Collections.Generic;
using Blueprinter;
using UnityEngine;

namespace NO_Server_Balancer;

internal static class LoggingUtils
{
    public static void LogLoadedLasers(Blueprinter.Plugin blueprinter)
    {
        var seenLasers = new HashSet<int>();
        
        foreach (KeyValuePair<string, LoadedBundle> bundleEntry in blueprinter.bundleRegistry.BundlesByName)
        {
            var bundleName = bundleEntry.Key;
            var loadedBundle = bundleEntry.Value;
            
            if (loadedBundle?.AssetBundle == null)
                continue;
            
            var assetBundle = loadedBundle.AssetBundle;
            string[] assetNames;
            
            try
            {
                assetNames = assetBundle.GetAllAssetNames();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[ServerBalancer] Could not enumerate bundle \"{bundleName}\": {ex}");
                
                continue;
            }
            
            foreach (var assetName in assetNames)
            {
                GameObject root;
                
                try
                {
                    root = assetBundle.LoadAsset<GameObject>(assetName);
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError(
                        $"[ServerBalancer] Could not load GameObject asset \"{assetName}\" from \"{bundleName}\": {ex}");
                    
                    continue;
                }
                
                // Many assets will not be GameObjects.
                if (!root)
                    continue;
                
                var lasers = root.GetComponentsInChildren<Laser>(true);
                
                foreach (var laser in lasers)
                {
                    if (!laser)
                        continue;
                    
                    // Avoid duplicate output if the same prefab can be reached
                    // through more than one loaded asset reference.
                    if (!seenLasers.Add(laser.GetInstanceID()))
                        continue;
                }
            }
        }
    }
    
    private static Transform? FindAncestorOrSelf(Transform? transform, string expectedName)
    {
        var current = transform;
        
        while (current != null)
        {
            if (Utils.HasUnityName(current.name, expectedName))
                return current;
            
            current = current.parent;
        }
        
        return null;
    }
    
    private static bool HasNamedAncestor(Transform transform, string expectedAncestorName, Transform stopAt)
    {
        var current = transform.parent;
        
        while (current != null)
        {
            if (Utils.HasUnityName(current.name, expectedAncestorName)) return true;
            
            if (current == stopAt)
                break;
            
            current = current.parent;
        }
        
        return false;
    }
    
    private static string GetTransformPath(Transform transform)
    {
        var names = new Stack<string>();
        var current = transform;
        
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }
        
        return string.Join("/", names);
    }
}