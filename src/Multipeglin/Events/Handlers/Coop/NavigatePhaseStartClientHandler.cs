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

        CoopNavigateState.StartPhase(
            networkEvent.Source,
            networkEvent.ChildNodeCount,
            totalVoters: 1, // client only knows about itself; not used for resolution on client
            now: Time.unscaledTime);

        Patches.MultiplayerClientPatches.AllowNavigateLogic = true;

        // Drop the post-rewards "waiting for other players" overlay so the player
        // can see and use their nav ball. Scene change later closes the overlay
        // for nav_only flows naturally; we clear the awaiting-host flags here too.
        CoopRewardState.WaitingForOtherPlayers = false;
        CoopRewardState.AllChoicesComplete = true;
        CoopRewardState.ShopAwaitingHostNavigation = false;
        CoopRewardState.TreasureAwaitingHostNavigation = false;
        CoopRewardState.TextScenarioAwaitingHostNavigation = false;
        CoopRewardState.PegMinigameAwaitingHostNavigation = false;

        try
        {
            if (networkEvent.Source == "nav_only")
            {
                InvokeNavOnlyPrepareForNavigation();
            }
            else
            {
                InvokePostBattleStartNavigation();
            }
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

    private static void InvokeNavOnlyPrepareForNavigation()
    {
        var nocs = Resources.FindObjectsOfTypeAll<global::NavOnlyController>();
        global::NavOnlyController target = null;
        if (nocs != null)
        {
            foreach (var n in nocs)
            {
                if (n != null && n.gameObject != null && n.gameObject.activeInHierarchy)
                {
                    target = n;
                    break;
                }
            }

            if (target == null && nocs.Length > 0)
            {
                target = nocs[0];
            }
        }

        if (target == null)
        {
            MultiplayerPlugin.Logger?.LogWarning("[CoopNavigate] Client: no NavOnlyController to prepare for navigation");
            return;
        }

        target.PrepareForNavigation();
        MultiplayerPlugin.Logger?.LogInfo("[CoopNavigate] Client invoked NavOnlyController.PrepareForNavigation");
    }
}
