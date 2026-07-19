using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace NO_Server_Balancer;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.nikkorap.blueprinter")]
public class Plugin : BaseUnityPlugin
{
    public const bool IsDebug = true;
    public new static ManualLogSource Logger { get; private set; } = null!;
    
    internal static ConfigFile AircraftPricesConfig { get; private set; } = null!;
    
    internal static Plugin Instance { get; private set; } = null!;
    
    private Harmony? Harmony { get; set; }
    
    private void Awake()
    {
        Instance = this;
        
        Logger = base.Logger;
        
        AircraftPricesConfig =
            new ConfigFile(Path.Combine(Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.AircraftPrices.cfg"), true,
                Info.Metadata);
        
        Harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        Repatch();
    }
    
    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }
    
    private void Repatch()
    {
        Harmony?.UnpatchSelf();
        Harmony?.PatchAll();
    }
}