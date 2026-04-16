using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Multipeglin.Core;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public static Plugin Instance { get; private set; }
    public new static ManualLogSource Logger { get; private set; }

    private Harmony _harmony;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll();

        Logger.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded");
    }
}
