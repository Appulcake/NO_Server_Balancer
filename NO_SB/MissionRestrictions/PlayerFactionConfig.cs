using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace NO_SB.MissionRestrictions;

internal static class PlayerFactionConfig
{
    private const int SupportedSchemaVersion = 1;
    
    private static readonly string ConfigPath =
        Path.Combine(Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.PlayerFactions.json");
    
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        Converters =
        {
            new StringEnumConverter()
        }
    };
    
    internal static bool TryRead(out ConfigFile config)
    {
        config = null!;
        
        if (!File.Exists(ConfigPath))
            return false;
        
        try
        {
            var json = File.ReadAllText(ConfigPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                Plugin.Logger.LogError($"Player faction config \"{ConfigPath}\" is empty.");
                return false;
            }
            
            var loaded = JsonConvert.DeserializeObject<ConfigFile>(json, JsonSettings);
            if (loaded == null)
            {
                Plugin.Logger.LogError("Player faction config deserialised to null.");
                return false;
            }
            
            if (loaded.SchemaVersion != SupportedSchemaVersion)
            {
                Plugin.Logger.LogError($"Unsupported player faction config schema version " +
                                       $"{loaded.SchemaVersion} " +
                                       $"(expected {SupportedSchemaVersion}).");
                return false;
            }
            
            config = loaded;
            return true;
        }
        catch (JsonException ex)
        {
            Plugin.Logger.LogError($"Invalid player faction config JSON:\n{ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            Plugin.Logger.LogError($"Could not read player faction config:\n{ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Plugin.Logger.LogError($"Could not access player faction config:\n{ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Unexpected error while loading player faction config:\n{ex}");
            return false;
        }
    }
    
    internal sealed class ConfigFile
    {
        [JsonProperty("schemaVersion", Required = Required.Always)]
        public int SchemaVersion { get; set; }
        
        [JsonProperty("missions", Required = Required.Always)]
        public List<MissionEntry> Missions { get; set; } = [];
    }
    
    internal sealed class MissionEntry
    {
        [JsonProperty("mission", Required = Required.Always)]
        public string Mission { get; set; } = string.Empty;
        
        [JsonProperty("playersPlayAs", Required = Required.Always)]
        public PlayerFactionMode PlayersPlayAs { get; set; }
        
        [JsonProperty("_comment")] public string? Comment { get; set; }
    }
    
    internal enum PlayerFactionMode
    {
        Both,
        Primeva,
        Boscali,
        Random
    }
}