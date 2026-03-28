using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PeglinMods.Spectator.DI;
using PeglinMods.Spectator.Network;
using PeglinMods.Spectator.Patches;
using PeglinMods.Spectator.Spectator;
using PeglinMods.Spectator.UI;
using PeglinMods.Spectator.Utility;
using UnityEngine;

namespace PeglinMods.Spectator;

[BepInPlugin(SpectatorPluginInfo.GUID, SpectatorPluginInfo.NAME, SpectatorPluginInfo.VERSION)]
[BepInDependency("com.peglinmods.core")]
public class SpectatorPlugin : BaseUnityPlugin
{
    public static SpectatorPlugin Instance { get; private set; }
    public static IServiceContainer Services { get; private set; }
    public new static ManualLogSource Logger { get; private set; }

    private Harmony _harmony;
    private FileLogger _fileLogger;
    private static GameObject _modObject;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        try
        {
            var logsDir = System.IO.Path.Combine(Paths.BepInExRootPath, "logs");
            _fileLogger = new FileLogger(logsDir);
            BepInEx.Logging.Logger.Listeners.Add(new FileLogListener(_fileLogger));
            Logger.LogInfo($"Log file: {_fileLogger.FilePath}");

            Services = ServiceRegistration.CreateAndConfigure(Logger);

            // Create a SEPARATE persistent object - the BepInEx_Manager gets
            // destroyed by the game during scene init (~2s after Awake).
            // HideAndDontSave prevents the game from finding and destroying it.
            // This is the same pattern ProLib and Promethium use.
            _modObject = new GameObject("PeglinMods_Spectator");
            _modObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_modObject);

            var poller = _modObject.AddComponent<NetworkPollBehaviour>();
            poller.Initialize(Services.Resolve<INetworkTransport>());

            var dispatcher = _modObject.AddComponent<MainThreadDispatcher>();
            Services.RegisterSingleton<MainThreadDispatcher>(dispatcher);

            _modObject.AddComponent<MultiplayerUI>();
            _modObject.AddComponent<SceneWatcher>();

            _harmony = new Harmony(SpectatorPluginInfo.GUID);
            _harmony.PatchAll();

            int patchCount = 0;
            foreach (var method in _harmony.GetPatchedMethods())
            {
                Logger.LogInfo($"Harmony patched: {method.DeclaringType?.FullName}.{method.Name}");
                patchCount++;
            }
            Logger.LogInfo($"Harmony total patches applied: {patchCount}");

            Logger.LogInfo($"{SpectatorPluginInfo.NAME} v{SpectatorPluginInfo.VERSION} loaded");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to initialize: {ex}");
        }
    }

    private void OnDestroy()
    {
        // BepInEx_Manager gets destroyed ~2s after startup, but our
        // _modObject survives because of HideAndDontSave.
        // Do NOT dispose fileLogger here - it's still needed by _modObject.
        // Do NOT unpatch Harmony - patches survive independently.
    }
}
