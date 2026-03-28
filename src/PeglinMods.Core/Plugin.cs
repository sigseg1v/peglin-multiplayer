using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace PeglinMods.Core;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public static Plugin Instance { get; private set; }
    public new static ManualLogSource Logger { get; private set; }

    private Harmony _harmony;
    private static string _diagPath;
    private bool _updateLogged;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        _diagPath = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
            "core_diag.txt");
        File.WriteAllText(_diagPath, $"[{DateTime.Now:HH:mm:ss.fff}] Core Awake() on '{gameObject.name}'\n");

        CrashReporterDisabler.Disable(Logger);

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll();

        Logger.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded");
        File.AppendAllText(_diagPath, $"[{DateTime.Now:HH:mm:ss.fff}] Awake complete\n");
    }

    private void Start()
    {
        File.AppendAllText(_diagPath, $"[{DateTime.Now:HH:mm:ss.fff}] Start() called\n");
    }

    private void Update()
    {
        if (!_updateLogged)
        {
            _updateLogged = true;
            File.AppendAllText(_diagPath, $"[{DateTime.Now:HH:mm:ss.fff}] Update() first call!\n");
            Logger.LogInfo("Core Update() is firing");
        }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        File.AppendAllText(_diagPath, $"[{DateTime.Now:HH:mm:ss.fff}] OnDestroy() called\n");
    }
}
