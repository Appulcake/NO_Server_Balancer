using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;

namespace NO_SB.LCAC_Limit;

internal sealed class VehicleLimit(int playerLimit, int factionLimit)
{
    public int PlayerLimit { get; } = playerLimit;
    public int FactionLimit { get; } = factionLimit;
}

internal sealed class VehicleLimitConfig
{
    private const string GeneralSection = "General";
    private const string VehicleLimitsSection = "Vehicle Limits";
    
    private static readonly string[] LcacVehicleNames =
    [
        "AFV6 AA",
        "AFV6 APC",
        "AFV6 AT",
        "AFV6 IFV",
        "AFV8 APC",
        "AFV8 IFV",
        "AFV8 Mobile Air Defense",
        "AeroSentry SPAAG",
        "FGA-57 Anvil",
        "HLT Fire Control",
        "HLT Fuel Tanker",
        "HLT Munitions Truck",
        "HLT Radar Truck",
        "HLT-CRAM",
        "HLT-HEL",
        "Hexhound GMG",
        "Hexhound SAM",
        "LCV25 AA",
        "LCV25 AT",
        "LCV45 Recon Truck",
        "Linebreaker APC",
        "Linebreaker IFV",
        "Linebreaker SAM",
        "M12 Jackknife",
        "MSV Ballistic Missile Launcher",
        "MSV CRAM",
        "MSV Fire Control",
        "MSV Fuel Tanker",
        "MSV LADS",
        "MSV MRAP",
        "MSV Munitions",
        "MSV Nuclear Ballistic Missile Launcher",
        "MSV R9 Stratolance Launcher",
        "MSV Radar",
        "Spearhead MBT",
        "StratoLance R9 Launcher",
        "T9K41 Boltstrike",
        "Type-12 MBT"
    ];
    
    private static readonly Dictionary<string, VehicleLimit>
        InitialDefaults =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["AFV6 AA"] = new VehicleLimit(10, 50),
                ["AFV6 APC"] = new VehicleLimit(10, 50),
                ["AFV6 AT"] = new VehicleLimit(10, 50),
                ["AFV6 IFV"] = new VehicleLimit(10, 50),
                ["AFV8 APC"] = new VehicleLimit(10, 50),
                ["AFV8 IFV"] = new VehicleLimit(0, 0),
                ["AFV8 Mobile Air Defense"] = new VehicleLimit(10, 50),
                ["AeroSentry SPAAG"] = new VehicleLimit(10, 50),
                ["FGA-57 Anvil"] = new VehicleLimit(10, 50),
                ["HLT Fire Control"] = new VehicleLimit(10, 50),
                ["HLT Fuel Tanker"] = new VehicleLimit(10, 50),
                ["HLT Munitions Truck"] = new VehicleLimit(10, 50),
                ["HLT Radar Truck"] = new VehicleLimit(10, 50),
                ["HLT-CRAM"] = new VehicleLimit(10, 50),
                ["HLT-HEL"] = new VehicleLimit(10, 50),
                ["Hexhound GMG"] = new VehicleLimit(10, 50),
                ["Hexhound SAM"] = new VehicleLimit(10, 50),
                ["LCV25 AA"] = new VehicleLimit(10, 50),
                ["LCV25 AT"] = new VehicleLimit(10, 50),
                ["LCV45 Recon Truck"] = new VehicleLimit(10, 50),
                ["Linebreaker APC"] = new VehicleLimit(0, 0),
                ["Linebreaker IFV"] = new VehicleLimit(0, 0),
                ["Linebreaker SAM"] = new VehicleLimit(0, 0),
                ["M12 Jackknife"] = new VehicleLimit(10, 50),
                ["MSV Ballistic Missile Launcher"] = new VehicleLimit(10, 50),
                ["MSV CRAM"] = new VehicleLimit(10, 50),
                ["MSV Fire Control"] = new VehicleLimit(10, 50),
                ["MSV Fuel Tanker"] = new VehicleLimit(10, 50),
                ["MSV LADS"] = new VehicleLimit(10, 50),
                ["MSV MRAP"] = new VehicleLimit(10, 50),
                ["MSV Munitions"] = new VehicleLimit(10, 50),
                ["MSV Nuclear Ballistic Missile Launcher"] = new VehicleLimit(10, 50),
                ["MSV R9 Stratolance Launcher"] = new VehicleLimit(10, 50),
                ["MSV Radar"] = new VehicleLimit(10, 50),
                ["Spearhead MBT"] = new VehicleLimit(10, 50),
                ["StratoLance R9 Launcher"] = new VehicleLimit(10, 50),
                ["T9K41 Boltstrike"] = new VehicleLimit(10, 50),
                ["Type-12 MBT"] = new VehicleLimit(10, 50)
            };
    
    private static bool _typeConverterRegistered;
    
    private readonly ConfigEntry<bool> _allowUnconfiguredVehicles;
    
    private readonly Dictionary<string, ConfigEntry<VehicleLimit>>
        _limitEntries = new(StringComparer.OrdinalIgnoreCase);
    
    internal VehicleLimitConfig(ConfigFile config)
    {
        RegisterVehicleLimitTypeConverter();
        
        _allowUnconfiguredVehicles = config.Bind(GeneralSection, "AllowUnconfiguredVehicles", true,
            new ConfigDescription(
                "Toggle whether LCAC deployable vehicles that have no predefined limit set may spawn. If true," +
                " unknown vehicles are allowed without being tracked. If false, they are denied."));
        
        _allowUnconfiguredVehicles.SettingChanged += (_, _) =>
        {
            Plugin.Logger.LogInfo($"AllowUnconfiguredVehicles changed to {_allowUnconfiguredVehicles.Value}.");
        };
        
        foreach (var vehicleName in LcacVehicleNames)
        {
            var defaultLimit = GetInitialDefault(vehicleName);
            
            var entry = config.Bind(VehicleLimitsSection, vehicleName, defaultLimit,
                new ConfigDescription(
                    "Deployment limits, format: PerPlayerLimit, FactionLimit. -1 means unlimited; 0 means none allowed."));
            
            _limitEntries.Add(vehicleName, entry);
            
            var capturedVehicleName = vehicleName;
            var capturedEntry = entry;
            
            capturedEntry.SettingChanged += (_, _) =>
            {
                var limit = capturedEntry.Value;
                
                Plugin.Logger.LogInfo(
                    $"Deployment limits for {capturedVehicleName} changed: player={limit.PlayerLimit}, faction={limit.FactionLimit}.");
            };
        }
        
        config.Save();
        
        Plugin.Logger.LogInfo($"Loaded deployment limits for {_limitEntries.Count} vehicle type(s).");
    }
    
    internal bool AllowUnconfiguredVehicles => _allowUnconfiguredVehicles.Value;
    
    internal bool TryGetLimit(string vehicleName, out VehicleLimit limit)
    {
        if (_limitEntries.TryGetValue(vehicleName, out var entry))
        {
            limit = entry.Value;
            return true;
        }
        
        limit = null!;
        return false;
    }
    
    private static VehicleLimit GetInitialDefault(string vehicleName) =>
        InitialDefaults.TryGetValue(vehicleName, out var limit) ? limit : new VehicleLimit(-1, -1);
    
    private static void RegisterVehicleLimitTypeConverter()
    {
        if (_typeConverterRegistered)
            return;
        
        var converter = new TypeConverter
        {
            ConvertToString = (value, _) =>
            {
                var limit = (VehicleLimit)value;
                
                return
                    limit.PlayerLimit.ToString(CultureInfo.InvariantCulture) +
                    ", " +
                    limit.FactionLimit.ToString(CultureInfo.InvariantCulture);
            },
            
            ConvertToObject = (text, _) => ParseVehicleLimit(text)
        };
        
        if (!TomlTypeConverter.AddConverter(typeof(VehicleLimit), converter))
            throw new InvalidOperationException(
                $"A BepInEx configuration converter is already registered for {nameof(VehicleLimit)}.");
        
        _typeConverterRegistered = true;
    }
    
    private static VehicleLimit ParseVehicleLimit(string text)
    {
        var values = text.Split(',');
        
        if (values.Length != 2 || !TryParseLimit(values[0], out var playerLimit) ||
            !TryParseLimit(values[1], out var factionLimit))
            throw new FormatException(
                $"Invalid vehicle limit value: {text}. Expected: PerPlayerLimit, FactionLimit. Both values must be -1 or greater.");
        
        return new VehicleLimit(playerLimit, factionLimit);
    }
    
    private static bool TryParseLimit(string text, out int value)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return false;
        
        return value >= -1;
    }
}