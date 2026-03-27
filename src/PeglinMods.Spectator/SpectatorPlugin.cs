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
    private GameObject _networkObj;
    private FileLogger _fileLogger;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        try
        {
            // File logging
            var logsDir = System.IO.Path.Combine(Paths.BepInExRootPath, "logs");
            _fileLogger = new FileLogger(logsDir);
            BepInEx.Logging.Logger.Listeners.Add(new FileLogListener(_fileLogger));
            Logger.LogInfo($"Log file: {_fileLogger.FilePath}");

            // DI container
            Services = ServiceRegistration.CreateAndConfigure(Logger);

            // Persistent game object for MonoBehaviour components
            _networkObj = new GameObject("PeglinMods_Spectator");
            DontDestroyOnLoad(_networkObj);

            // Network polling (calls transport.PollEvents() every frame)
            var poller = _networkObj.AddComponent<NetworkPollBehaviour>();
            poller.Initialize(Services.Resolve<INetworkTransport>());

            // Main thread dispatcher for network callbacks
            var dispatcher = _networkObj.AddComponent<MainThreadDispatcher>();
            Services.RegisterSingleton<MainThreadDispatcher>(dispatcher);

            // Multiplayer UI overlay (F7 hotkey, host/join panels)
            _networkObj.AddComponent<MultiplayerUI>();

            // Scene watcher - detects scene changes via SceneManager.sceneLoaded
            // and injects the Multiplayer button into the main menu
            _networkObj.AddComponent<SceneWatcher>();

            // Harmony patches
            _harmony = new Harmony(SpectatorPluginInfo.GUID);
            _harmony.PatchAll();

            Logger.LogInfo($"{SpectatorPluginInfo.NAME} v{SpectatorPluginInfo.VERSION} loaded");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to initialize: {ex}");
        }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        try { Services?.Resolve<INetworkTransport>()?.Stop(); } catch { }
        _fileLogger?.Dispose();
        if (_networkObj != null) Destroy(_networkObj);
    }
}
