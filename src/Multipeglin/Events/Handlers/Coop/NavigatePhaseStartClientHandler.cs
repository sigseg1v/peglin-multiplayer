using HarmonyLib;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client handler for NavigatePhaseStartEvent: enters the parallel-shoot navigate
/// phase locally. Sets AllowNavigateLogic, clears the "waiting for other players"
/// overlay, and invokes the native nav-arming method (PostBattleController.StartNavigation
/// for "post_battle" or NavOnlyController.PrepareForNavigation for "nav_only") so
/// the slots configure and the nav ball arms on the client.
/// </summary>
public sealed class NavigatePhaseStartClientHandler : IClientHandler<NavigatePhaseStartEvent>
{
    public void Handle(NavigatePhaseStartEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return;
        }

        // Host runs the resolver locally; this handler only fires on clients.
        if (services.TryResolve<IMultiplayerMode>(out var mode) && mode.IsHosting)
        {
            return;
        }

        MultiplayerPlugin.Logger?.LogInfo(
            $"[CoopNavigate] Client received navigate phase start: source={networkEvent.Source}, children={networkEvent.ChildNodeCount}");

        // Defensive: nav_only sources (treasure/shop/text/peg-minigame) are
        // host-solo by contract — the host runs the nav-shot natively after
        // AllChoicesComplete and the scene transitions. Clients do NOT have
        // NavOnlyController properly wired (no chest Skip / shop CloseStore
        // ran locally) so arming a nav ball here NREs. If a stray nav_only
        // event arrives anyway (older host build / out-of-order delivery),
        // log it and stay in the awaiting-host overlay; the map sync will
        // pull us onto the next scene shortly.
        if (networkEvent.Source == "nav_only")
        {
            MultiplayerPlugin.Logger?.LogWarning(
                "[CoopNavigate] Ignoring nav_only phase start on client — host runs nav-shot solo by lockstep contract");
            return;
        }

        CoopNavigateState.StartPhase(
            networkEvent.Source,
            networkEvent.ChildNodeCount,
            totalVoters: 1, // client only knows about itself; not used for resolution on client
            now: Time.unscaledTime);

        Patches.MultiplayerClientPatches.AllowNavigateLogic = true;

        // Drop the post-rewards "waiting for other players" overlay so the player
        // can see and use their nav ball. (post_battle path only — nav_only
        // returned above so these flags stay set and the overlay persists.)
        CoopRewardState.WaitingForOtherPlayers = false;
        CoopRewardState.AllChoicesComplete = true;

        try
        {
            InvokePostBattleStartNavigation();
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError(
                $"[CoopNavigate] Client failed to arm nav ball: {ex.Message}");
        }
    }

    private static void InvokePostBattleStartNavigation()
    {
        var pbcs = Resources.FindObjectsOfTypeAll<global::Battle.PostBattleController>();
        global::Battle.PostBattleController target = null;
        if (pbcs != null)
        {
            foreach (var p in pbcs)
            {
                if (p != null && p.gameObject != null && p.gameObject.activeInHierarchy)
                {
                    target = p;
                    break;
                }
            }

            if (target == null && pbcs.Length > 0)
            {
                target = pbcs[0];
            }
        }

        if (target == null)
        {
            MultiplayerPlugin.Logger?.LogWarning("[CoopNavigate] Client: no PostBattleController to start navigation on");
            return;
        }

        var startNavigation = AccessTools.Method(typeof(global::Battle.PostBattleController), "StartNavigation");
        if (startNavigation == null)
        {
            MultiplayerPlugin.Logger?.LogWarning("[CoopNavigate] Client: StartNavigation method not found");
            return;
        }

        startNavigation.Invoke(target, new object[] { true });
        MultiplayerPlugin.Logger?.LogInfo("[CoopNavigate] Client invoked PostBattleController.StartNavigation");
    }
}
