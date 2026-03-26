using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PeglinMods.Spectator.DI;
using PeglinMods.Spectator.Network;
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
            var logsDir = System.IO.Path.Combine(Paths.BepInExRootPath, "logs");
            _fileLogger = new FileLogger(logsDir);
            BepInEx.Logging.Logger.Listeners.Add(new FileLogListener(_fileLogger));
            Logger.LogInfo($"Log file: {_fileLogger.FilePath}");

            Services = ServiceRegistration.CreateAndConfigure(Logger);

            _networkObj = new GameObject("PeglinMods_Spectator");
            DontDestroyOnLoad(_networkObj);

            var poller = _networkObj.AddComponent<NetworkPollBehaviour>();
            poller.Initialize(Services.Resolve<INetworkTransport>());

            var dispatcher = _networkObj.AddComponent<MainThreadDispatcher>();
            Services.RegisterSingleton<MainThreadDispatcher>(dispatcher);

            _networkObj.AddComponent<MultiplayerUI>();

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
