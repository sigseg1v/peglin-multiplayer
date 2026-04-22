using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.DI;
using Multipeglin.GameState;
using Multipeglin.Network;
using Multipeglin.Patches;
using Multipeglin.Multiplayer;
using Multipeglin.UI;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin;

[BepInPlugin(MultiplayerPluginInfo.GUID, MultiplayerPluginInfo.NAME, MultiplayerPluginInfo.VERSION)]
[BepInDependency("com.multipeglin.core")]
public class MultiplayerPlugin : BaseUnityPlugin
{
    public static MultiplayerPlugin Instance { get; private set; }
    public static IServiceContainer Services { get; private set; }
    public new static ManualLogSource Logger { get; private set; }

    /// <summary>
    /// Patch targets that were declared but not found at runtime.
    /// Non-null and non-empty means the game may have been updated.
    /// </summary>
    public static IReadOnlyList<string> MissingPatches { get; private set; }

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
            _modObject = new GameObject("Multipeglin");
            _modObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_modObject);

            var poller = _modObject.AddComponent<NetworkPollBehaviour>();
            poller.Initialize(Services.Resolve<INetworkTransport>());

            var dispatcher = _modObject.AddComponent<MainThreadDispatcher>();
            Services.RegisterSingleton<MainThreadDispatcher>(dispatcher);

            _modObject.AddComponent<MultiplayerUI>();
            _modObject.AddComponent<SceneWatcher>();
            _modObject.AddComponent<BallPositionSync>();
            _modObject.AddComponent<HostBallRegistry>();
            _modObject.AddComponent<ClientBallRenderer>();
            _modObject.AddComponent<ClientAimRenderer>();
            _modObject.AddComponent<CursorSync>();
            _modObject.AddComponent<RemoteCursorRenderer>();
            _modObject.AddComponent<ClientAttackProjectile>();
            _modObject.AddComponent<CoopPlayerVisuals>();
            _modObject.AddComponent<PendingDamageOverlay>();
            _modObject.AddComponent<SkipTurnButton>();

            _modObject.AddComponent<CoopRewardUI>();
            _modObject.AddComponent<ClientRelicChoiceApplier>();

            _harmony = new Harmony(MultiplayerPluginInfo.GUID);
            try
            {
                _harmony.PatchAll();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Harmony PatchAll failed: {ex}");
                MissingPatches = new List<string> { $"PatchAll failed: {ex.Message}" };
            }

            int patchCount = 0;
            foreach (var method in _harmony.GetPatchedMethods())
            {
                Logger.LogInfo($"Harmony patched: {method.DeclaringType?.FullName}.{method.Name}");
                patchCount++;
            }
            Logger.LogInfo($"Harmony total patches applied: {patchCount}");

            // Validate all declared patch targets exist at runtime
            if (MissingPatches == null)
            {
                var missing = PatchValidator.FindMissingPatchTargets(Logger);
                if (missing.Count > 0)
                {
                    Logger.LogWarning($"[PatchValidator] {missing.Count} patch target(s) not found:");
                    foreach (var p in missing)
                        Logger.LogWarning($"  Missing: {p}");
                    MissingPatches = missing;
                }
            }

            Logger.LogInfo($"{MultiplayerPluginInfo.NAME} v{MultiplayerPluginInfo.VERSION} loaded");
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
