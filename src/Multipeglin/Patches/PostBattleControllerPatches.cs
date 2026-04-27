using HarmonyLib;
using Multipeglin.Events;
using Multipeglin.Events.Handlers.Coop;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class PostBattleControllerPatches
{
    // =========================================================================
    // POST-BATTLE REWARD PHASE — intercept navigation for coop sync
    // =========================================================================

    /// <summary>
    /// Intercept PostBattleController.StartNavigation to synchronize the coop
    /// post-battle reward phase. On host: delay navigation until all clients
    /// finish. On client: block navigation entirely and send results to host.
    /// </summary>
    [HarmonyPatch(typeof(Battle.PostBattleController), "StartNavigation")]
    [HarmonyPrefix]
    public static bool PostBattleController_StartNavigation_Prefix(Battle.PostBattleController __instance)
    {
        if (!UI.LobbyUI.GameStartReceived)
        {
            return true; // not coop
        }

        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return true;
        }

        if (IsHosting)
        {
            if (!Events.Handlers.Coop.CoopRewardState.HostRewardPhaseActive)
            {
                return true; // not in coop reward phase — normal flow
            }

            // Save host's updated state after rewards
            if (services.TryResolve<GameState.CoopStateManager>(out var coopState))
            {
                coopState.SaveActivePlayerState();
            }

            Events.Handlers.Coop.CoopRewardState.HostRewardsDone = true;
            Events.Handlers.Coop.CoopRewardState.PendingPostBattleController = __instance;

            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Host finished post-battle rewards, checking if all clients done...");

            if (Events.Handlers.Coop.CoopRewardState.AllClientRewardChoicesReceived)
            {
                // All done — proceed with navigation
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] All clients done — proceeding with navigation");
                Events.Handlers.Coop.CoopRewardState.HostRewardPhaseActive = false;
                AllowNativeRewardLogic = false;

                // Strip negative debuffs from all players before leaving the battle scene
                if (services.TryResolve<GameState.CoopStateManager>(out var coopState2))
                {
                    coopState2.ClearNegativeDebuffsFromAllPlayers();
                }

                if (services.TryResolve<IGameEventRegistry>(out var reg))
                {
                    reg.Dispatch(new Events.Network.Coop.AllChoicesCompleteEvent { Phase = "post_battle" });
                }

                // Enter parallel-shoot navigate phase. Returns false in solo / single-player
                // contexts (no clients), in which case the host runs the native flow alone.
                var childCount = StaticGameData.currentNode?.ChildNodes?.Length ?? 0;
                CoopNavigateResolver.StartPhase("post_battle", childCount);

                return true; // let StartNavigation run
            }
            else
            {
                // Still waiting for clients
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[ClientPatches] Waiting for clients: {Events.Handlers.Coop.CoopRewardState.ClientRewardChoicesReceived.Count}/{Events.Handlers.Coop.CoopRewardState.TotalRewardClientsExpected}");
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                return false; // block navigation until all clients done
            }
        }
        else if (ShouldSuppressClientLogic)
        {
            // When the parallel-shoot navigate phase is active, this is the client
            // running StartNavigation locally (invoked by NavigatePhaseStartClientHandler).
            // Allow it through so the slots get configured and the nav ball arms.
            if (CoopNavigateState.PhaseActive && AllowNavigateLogic)
            {
                return true;
            }

            // Client: never navigate — send results to host and wait
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Client finished post-battle rewards — sending results to host");

            try
            {
                var completeEvent = CaptureClientPostBattleState();
                if (services.TryResolve<Network.IMessageSender>(out var sender))
                {
                    sender.Send(completeEvent);
                }
            }
            catch (System.Exception ex)
            {
                MultiplayerPlugin.Logger?.LogError($"[ClientPatches] Failed to send PostBattleCompleteEvent: {ex.Message}");
            }

            // Disable reward logic bypass now that the screen is done
            AllowNativeRewardLogic = false;

            // Clear relic choice tracking
            ClientChosenPostBattleRelicEffect = -1;
            ClientChosenPostBattleRelicName = null;
            Events.Handlers.Coop.CoopRewardState.PendingPostBattleRelicChoices = null;

            // Show waiting overlay
            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
            return false; // never navigate on client
        }

        return true;
    }
}
