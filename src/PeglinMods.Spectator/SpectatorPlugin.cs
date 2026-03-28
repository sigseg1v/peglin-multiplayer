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

            // Add all MonoBehaviour components to THIS gameObject (the BepInEx manager).
            // Creating a separate new GameObject during chainloader doesn't get Unity
            // update loop registration. The BepInEx manager object IS in the loop.
            var poller = gameObject.AddComponent<NetworkPollBehaviour>();
            poller.Initialize(Services.Resolve<INetworkTransport>());

            var dispatcher = gameObject.AddComponent<MainThreadDispatcher>();
            Services.RegisterSingleton<MainThreadDispatcher>(dispatcher);

            gameObject.AddComponent<MultiplayerUI>();
            gameObject.AddComponent<SceneWatcher>();

            // Harmony patches
            _harmony = new Harmony(SpectatorPluginInfo.GUID);
            _harmony.PatchAll();

            // Verify what Harmony actually patched
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
        _harmony?.UnpatchSelf();
        try { Services?.Resolve<INetworkTransport>()?.Stop(); } catch { }
        _fileLogger?.Dispose();
    }
}
