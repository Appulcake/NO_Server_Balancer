using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NO_SB.LCAC_Limit;

namespace NO_SB;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.nikkorap.blueprinter")]
[BepInDependency("NOComponentsWIP")]
public class Plugin : BaseUnityPlugin
{
    public new static ManualLogSource Logger { get; private set; } = null!;
    
    internal static ConfigFile AircraftPricesConfig { get; private set; } = null!;
    private static ConfigFile BoteDeploymentLimitsConfig { get; set; } = null!;
    
    private Harmony? Harmony { get; set; }
    
    private void Awake()
    {
        Logger = base.Logger;
        
        AircraftPricesConfig =
            new ConfigFile(Path.Combine(Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.AircraftPrices.cfg"), true,
                Info.Metadata);
        
        var boteLimitConfigPath =
            Path.Combine(Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.BoteDeploymentLimits.cfg");
        
        BoteDeploymentLimitsConfig = new ConfigFile(boteLimitConfigPath, true, Info.Metadata);
        
        var limitConfig = new VehicleLimitConfig(BoteDeploymentLimitsConfig);
        
        LimitDeployment.Initialise(limitConfig);
        
        Harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        Repatch();
    }
    
    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
        
        LimitDeployment.ClearAllCounts("Plugin unloaded");
    }
    
    private void Repatch()
    {
        Harmony?.UnpatchSelf();
        Harmony?.PatchAll();
    }
}