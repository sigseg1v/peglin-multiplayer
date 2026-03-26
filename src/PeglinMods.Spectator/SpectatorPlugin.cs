using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PeglinMods.Spectator.DI;
using PeglinMods.Spectator.Network;
using PeglinMods.Spectator.Spectator;
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

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        Services = ServiceRegistration.CreateAndConfigure(Logger);

        _networkObj = new GameObject("PeglinMods_Spectator");
        DontDestroyOnLoad(_networkObj);

        var poller = _networkObj.AddComponent<NetworkPollBehaviour>();
        poller.Initialize(Services.Resolve<INetworkTransport>());

        var dispatcher = _networkObj.AddComponent<MainThreadDispatcher>();
        Services.RegisterSingleton<MainThreadDispatcher>(dispatcher);

        _harmony = new Harmony(SpectatorPluginInfo.GUID);
        _harmony.PatchAll();

        Logger.LogInfo($"{SpectatorPluginInfo.NAME} v{SpectatorPluginInfo.VERSION} loaded");
        Logger.LogInfo("Use BepInEx console to type 'host' or 'join <ip>' (not yet implemented - API ready)");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        try { Services?.Resolve<INetworkTransport>()?.Stop(); } catch { }
        if (_networkObj != null) Destroy(_networkObj);
    }
}
