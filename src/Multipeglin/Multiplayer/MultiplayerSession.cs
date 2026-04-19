using System;
using BepInEx.Logging;
using Multipeglin.DI;
using Multipeglin.Events;
using Multipeglin.Events.Handlers;
using Multipeglin.Events.Network;
using Multipeglin.GameState;
using Multipeglin.GameState.Appliers;
using Multipeglin.GameState.Providers;
using Multipeglin.Network;
using Multipeglin.Patches;
using Multipeglin.UI;
using Multipeglin.Utility;
using LobbyUI = Multipeglin.UI.LobbyUI;
using UnityEngine.SceneManagement;

namespace Multipeglin.Multiplayer;

/// <summary>
/// Central disconnect and state reset logic. Any disconnect (voluntary or involuntary)
/// from either side triggers a full cleanup and return to main menu.
/// </summary>
public static class MultiplayerSession
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    /// <summary>Guard against re-entrant disconnect calls.</summary>
    private static bool _disconnecting;

    /// <summary>
    /// Send a DisconnectEvent to the remote peer, then stop networking,
    /// reset all multiplayer state, and return to the main menu.
    /// </summary>
    public static void DisconnectAndReset(string reason = null)
    {
        if (_disconnecting) return;
        _disconnecting = true;

        try
        {
            Log?.LogInfo($"[Session] DisconnectAndReset: {reason ?? "no reason"}");

            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                _disconnecting = false;
                return;
            }

            // 1. Try to send a disconnect message to the remote before we stop
            TrySendDisconnect(services, reason);

            // 2. Stop the network transport
            if (services.TryResolve<INetworkTransport>(out var transport))
            {
                transport.Stop();
                Log?.LogInfo("[Session] Transport stopped");
            }

            // 3. Reset multiplayer mode (disables ShouldSuppressClientLogic)
            if (services.TryResolve<IMultiplayerMode>(out var mode))
            {
                mode.Disable();
                Log?.LogInfo("[Session] Mode disabled");
            }

            // 4. Reset remote peer info
            RemotePeerInfo.Reset();

            // 5. Reset all static state across the mod
            ResetStaticState(services);

            // 6. Clear player registry, coop state, and lobby state
            if (services.TryResolve<PlayerRegistry>(out var playerRegistry))
                playerRegistry.Clear();
            if (services.TryResolve<GameState.CoopStateManager>(out var coopState))
                coopState.Reset();
            LobbyUI.Reset();

            // 7. Clear event feed + remote cursors
            EventFeed.Clear();
            GameState.RemoteCursorRenderer.Instance?.ClearAll();

            // 7. Reset file logger role tag
            FileLogger.RoleTag = null;

            // 8. Load main menu if not already there
            var currentScene = SceneManager.GetActiveScene().name;
            if (currentScene != "MainMenu")
            {
                Log?.LogInfo($"[Session] Loading MainMenu (was on '{currentScene}')");
                try
                {
                    // Mode is already disabled, so scene load blocking is inactive.
                    // Use PeglinSceneLoader if available for proper fade/transition.
                    var loader = Loading.PeglinSceneLoader.Instance;
                    if (loader != null)
                    {
                        loader.LoadScene(Loading.PeglinSceneLoader.Scene.MAIN_MENU,
                            LoadSceneMode.Single, true, 0f);
                    }
                    else
                    {
                        SceneManager.LoadScene("MainMenu");
                    }
                }
                catch (Exception ex)
                {
                    Log?.LogWarning($"[Session] Failed to load MainMenu: {ex.Message}");
                    // Last resort fallback
                    try { SceneManager.LoadScene("MainMenu"); } catch { }
                }
            }

            Log?.LogInfo("[Session] Disconnect complete");
        }
        catch (Exception ex)
        {
            Log?.LogError($"[Session] DisconnectAndReset failed: {ex}");
        }
        finally
        {
            _disconnecting = false;
        }
    }

    private static void TrySendDisconnect(IServiceContainer services, string reason)
    {
        try
        {
            if (!services.TryResolve<INetworkTransport>(out var transport)) return;
            if (!transport.IsConnected) return;
            if (!services.TryResolve<IGameEventRegistry>(out var registry)) return;

            registry.Dispatch(new DisconnectEvent { Reason = reason ?? "Disconnected" });
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[Session] Failed to send disconnect event: {ex.Message}");
        }
    }

    private static void ResetStaticState(IServiceContainer services)
    {
        // MultiplayerClientPatches static fields
        MultiplayerClientPatches.CapturedPreMapGenRngState = null;
        MultiplayerClientPatches.PendingRngStateToRestore = null;
        MultiplayerClientPatches.PendingBattleRngState = null;
        MultiplayerClientPatches.MapControllerStartCompleted = false;
        MultiplayerClientPatches.AllowNextSceneLoad = false;
        MultiplayerClientPatches.AllowStatusEffectSync = false;

        // MapStateApplier and MapStateProvider static state
        MapStateApplier.ResetAllState();
        MapStateProvider.ResetCachedState();

        // GUID registries
        if (services.TryResolve<EnemyIdentifier>(out var enemyId))
            enemyId.Clear();
        if (services.TryResolve<PegIdentifier>(out var pegId))
            pegId.Clear();

        // GameStateApplyService — reset internal queued state
        if (services.TryResolve<GameStateApplyService>(out var applyService))
            applyService.Reset();

        Log?.LogInfo("[Session] Static state reset");
    }
}
