using System.Collections.Generic;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Host-only orchestration for the parallel-shoot navigate phase.
///   StartPhase: arms host's nav UI, broadcasts NavigatePhaseStartEvent.
///   RecordHostVote / RecordClientVote: tally + broadcast NavigateVoteUpdateEvent.
///   TryResolveIfDone: if all votes in (or ForceSkip), pick winner and run native flow.
/// </summary>
public static class CoopNavigateResolver
{
    /// <summary>
    /// Called by the host when entering the native navigate phase. Returns true if
    /// the parallel-shoot phase started; false if no clients are connected or coop
    /// is inactive (caller should let native flow proceed normally).
    /// </summary>
    public static bool StartPhase(string source, int childNodeCount)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return false;
        }

        if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting)
        {
            return false;
        }

        if (!services.TryResolve<PlayerRegistry>(out var registry))
        {
            return false;
        }

        var totalVoters = registry.SlotCount; // host + clients
        if (totalVoters <= 1)
        {
            return false; // solo — no need for vote system
        }

        if (childNodeCount < 1)
        {
            childNodeCount = 1;
        }

        CoopNavigateState.StartPhase(source, childNodeCount, totalVoters, Time.unscaledTime);

        if (services.TryResolve<IGameEventRegistry>(out var reg))
        {
            reg.Dispatch(new NavigatePhaseStartEvent
            {
                Source = source,
                ChildNodeCount = childNodeCount,
            });
        }

        MultiplayerPlugin.Logger?.LogInfo(
            $"[CoopNavigate] Phase started: source={source}, children={childNodeCount}, voters={totalVoters}");
        return true;
    }

    /// <summary>Called when the HOST's own nav ball lands on a slot.</summary>
    public static void RecordHostVote(int childIndex)
    {
        if (!CoopNavigateState.PhaseActive || CoopNavigateState.Resolved)
        {
            return;
        }

        if (CoopNavigateState.RecordVote(0, childIndex))
        {
            CoopNavigateState.LocalVoteCast = true;
            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopNavigate] Host voted child={childIndex} ({CoopNavigateState.VotedSlots.Count}/{CoopNavigateState.TotalVotersExpected})");
            BroadcastTallyUpdate();
            TryResolveIfDone(forceSkip: false);
        }
    }

    /// <summary>Called by NavigateVoteServerHandler when a client's vote arrives.</summary>
    public static void RecordClientVote(int slotIndex, int childIndex)
    {
        if (!CoopNavigateState.PhaseActive || CoopNavigateState.Resolved)
        {
            return;
        }

        if (CoopNavigateState.RecordVote(slotIndex, childIndex))
        {
            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopNavigate] Slot {slotIndex} voted child={childIndex} ({CoopNavigateState.VotedSlots.Count}/{CoopNavigateState.TotalVotersExpected})");
            BroadcastTallyUpdate();
            TryResolveIfDone(forceSkip: false);
        }
    }

    /// <summary>Called when a peer disconnects mid-phase — drop them from the expected count.</summary>
    public static void OnPeerDisconnected()
    {
        if (!CoopNavigateState.PhaseActive || CoopNavigateState.Resolved)
        {
            return;
        }

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<PlayerRegistry>(out var registry) == true)
        {
            CoopNavigateState.TotalVotersExpected = registry.SlotCount;
            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopNavigate] Voter count adjusted on disconnect -> {CoopNavigateState.TotalVotersExpected}");
            TryResolveIfDone(forceSkip: false);
        }
    }

    /// <summary>
    /// Host force-skip (60s button). Resolves with whatever votes exist. If zero
    /// votes, picks a random child index.
    /// </summary>
    public static void ForceResolve()
    {
        if (!CoopNavigateState.PhaseActive || CoopNavigateState.Resolved)
        {
            return;
        }

        MultiplayerPlugin.Logger?.LogWarning("[CoopNavigate] Host force-resolving navigate phase");
        TryResolveIfDone(forceSkip: true);
    }

    private static void BroadcastTallyUpdate()
    {
        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<IGameEventRegistry>(out var reg) == true)
        {
            reg.Dispatch(new NavigateVoteUpdateEvent
            {
                VoteCounts = new List<int>(CoopNavigateState.VoteCounts),
            });
        }
    }

    private static void TryResolveIfDone(bool forceSkip)
    {
        if (CoopNavigateState.Resolved)
        {
            return;
        }

        if (!forceSkip && !CoopNavigateState.AllVotesIn)
        {
            return;
        }

        var winner = PickWinner(CoopNavigateState.VoteCounts);
        CoopNavigateState.Resolved = true;
        CoopNavigateState.ChosenChildIndex = winner;

        MultiplayerPlugin.Logger?.LogInfo(
            $"[CoopNavigate] Resolved (forceSkip={forceSkip}): chose child={winner}, tally=[{string.Join(",", CoopNavigateState.VoteCounts)}]");

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<IGameEventRegistry>(out var reg) == true)
        {
            reg.Dispatch(new NavigateResolvedEvent
            {
                ChosenChildIndex = winner,
                VoteCounts = new List<int>(CoopNavigateState.VoteCounts),
            });
        }

        StaticGameData.chosenNextNodeIndex = winner;

        // Capture the source before resetting — the native invocation below
        // reads the phase, but Reset() clears it. Resetting here (before the
        // native scene transition starts) is what fixes the "host stuck at
        // shooter on Treasure" softlock: PachinkoBall_Fire_NavigateGuard
        // blocks Fire when LocalVoteCast || Resolved is set, so a stale phase
        // here would silently kill the host's next nav-ball click after we
        // arrive at Treasure / Shop / TextScenario / etc.
        var capturedSource = CoopNavigateState.Source;
        CoopNavigateState.Reset();

        // Trigger native scene transition based on phase source.
        if (capturedSource == "post_battle")
        {
            InvokePostBattleVictory();
        }
        else if (capturedSource == "nav_only")
        {
            InvokeNavOnlyFadeAndLoad();
        }
    }

    private static int PickWinner(List<int> votes)
    {
        if (votes == null || votes.Count == 0)
        {
            return 0;
        }

        var max = -1;
        for (var i = 0; i < votes.Count; i++)
        {
            if (votes[i] > max)
            {
                max = votes[i];
            }
        }

        var ties = new List<int>();
        for (var i = 0; i < votes.Count; i++)
        {
            if (votes[i] == max)
            {
                ties.Add(i);
            }
        }

        if (ties.Count == 1)
        {
            return ties[0];
        }

        // Tie (including the all-zero case) — pick uniformly at random.
        return ties[Random.Range(0, ties.Count)];
    }

    private static void InvokePostBattleVictory()
    {
        try
        {
            var pbcs = Resources.FindObjectsOfTypeAll<global::Battle.PostBattleController>();
            global::Battle.PostBattleController target = null;
            foreach (var p in pbcs)
            {
                if (p != null && p.gameObject != null && p.gameObject.activeInHierarchy)
                {
                    target = p;
                    break;
                }
            }

            if (target == null && pbcs != null && pbcs.Length > 0)
            {
                target = pbcs[0];
            }

            if (target == null)
            {
                MultiplayerPlugin.Logger?.LogWarning("[CoopNavigate] No PostBattleController found to trigger victory");
                return;
            }

            // SubmitVote moved the host's BattleController state to NAVIGATION_COMPLETE
            // (to prevent ball respawn after voting). TriggerVictory only runs when
            // CurrentBattleState == NAVIGATION, so swing it back briefly so the
            // native victory flow can fire.
            global::Battle.BattleController.CurrentBattleState = global::Battle.BattleController.BattleState.NAVIGATION;

            var triggerVictory = HarmonyLib.AccessTools.Method(typeof(global::Battle.PostBattleController), "TriggerVictory");
            triggerVictory?.Invoke(target, null);
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[CoopNavigate] InvokePostBattleVictory failed: {ex.Message}");
        }
    }

    private static void InvokeNavOnlyFadeAndLoad()
    {
        try
        {
            var nocs = Resources.FindObjectsOfTypeAll<global::NavOnlyController>();
            // Pick the active one (there can be inactive prefabs)
            global::NavOnlyController target = null;
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

            if (target == null)
            {
                MultiplayerPlugin.Logger?.LogWarning("[CoopNavigate] No NavOnlyController found to FadeAndLoad");
                return;
            }

            // Treasure-scene bonus: if all chest bombs were detonated before the
            // navigate phase resolved, the native flow loads back into TREASURE
            // and triggers DoBonusChestStuff. Replicate that here so the perk
            // isn't lost just because we hijacked HandleSlotTriggerActivated.
            if (target.isTreasureScene && AllTreasureBombsDetonated(target))
            {
                var chest = UnityEngine.Object.FindObjectOfType<global::Scenarios.ChestScenarioController>();
                chest?.DoBonusChestStuff();

                target.FadeAndLoad(global::Loading.PeglinSceneLoader.Scene.TREASURE);
                return;
            }

            target.FadeAndLoad();
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[CoopNavigate] InvokeNavOnlyFadeAndLoad failed: {ex.Message}");
        }
    }

    private static bool AllTreasureBombsDetonated(global::NavOnlyController noc)
    {
        var bombsField = HarmonyLib.AccessTools.Field(typeof(global::NavOnlyController), "_bombs");
        var bombs = bombsField?.GetValue(noc) as global::Bomb[];
        if (bombs == null || bombs.Length == 0)
        {
            return false;
        }

        foreach (var b in bombs)
        {
            if (b != null && !b.detonated)
            {
                return false;
            }
        }

        return true;
    }
}
