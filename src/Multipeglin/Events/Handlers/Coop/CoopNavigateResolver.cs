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
    /// <summary>
    /// Capture per-child RoomType ints from StaticGameData.currentNode (host-only).
    /// Length matches the live child count; falls back to all-zero (NONE) if the
    /// node tree is missing.
    /// </summary>
    public static int[] CaptureChildRoomTypes()
    {
        var node = StaticGameData.currentNode;
        if (node?.ChildNodes == null || node.ChildNodes.Length == 0)
        {
            return System.Array.Empty<int>();
        }

        var result = new int[node.ChildNodes.Length];
        for (var i = 0; i < node.ChildNodes.Length; i++)
        {
            var child = node.ChildNodes[i];
            result[i] = child != null ? (int)child.RoomType : 0;
        }

        return result;
    }

    public static bool StartPhase(string source, int childNodeCount, int[] childRoomTypes = null)
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

        CoopNavigateState.StartPhase(source, childNodeCount, totalVoters, Time.unscaledTime, childRoomTypes);

        // =====================================================================
        // NAV-PHASE RELIC MERGE — DELIBERATE SIMPLIFICATION
        // =====================================================================
        // During the post-battle parallel-shoot navigate phase, we force slot 0
        // (the host) to be the ACTIVE player and we MERGE every other player's
        // owned relics into slot 0's RelicManager for the duration of the phase.
        //   - No turn swapping. Slot 0 stays active until the phase resolves.
        //   - All players still fire their own nav-ball (parallel) — nav balls
        //     have NO orb effects, so we don't merge decks/orbs/status effects.
        //   - The merged relic set is what the singleton sees while any nav
        //     ball is in flight. UnmergeNavPhaseRelics() pops exactly the added
        //     relics off in TryResolveIfDone() before the scene transition, so
        //     each player keeps their own relic loadout for the next stage.
        //
        // WHY: the alternative (per-player active swaps mid-phase, separate
        // RelicManager state per nav ball) was a deadlock-prone tarpit — saw
        // 0/4 votes after 35s, black-screen clients, "Your turn!" banner stuck
        // on the host. Players want to shoot and go; this avoids the wait.
        //
        // TRADEOFF: any relic with battle-time side effects (peg-spawn, ball-
        // spawn, on-shot triggers) will fire under the union set during the
        // nav phase. Acceptable because nav balls don't trigger most of these
        // (no peg interactions in the same way, no orb attached). If a future
        // relic breaks under this model, see "TO UNDO" below.
        //
        // TO UNDO this design (revert to per-player nav state):
        //   1. Remove the MergeAllRelicsForNavPhase()/UnmergeNavPhaseRelics()
        //      calls below and in TryResolveIfDone (search for both).
        //   2. Drop the SwapToPlayer(0) force here. Re-enable turn swapping
        //      for the nav phase in TurnManager (search for nav-phase guards
        //      added when this was introduced).
        //   3. Make NavigatePhaseStartClientHandler load each client's own
        //      CoopPlayerState into their local singletons before arming the
        //      nav ball — clients currently shoot under whatever state was
        //      last loaded; a per-player model needs explicit state load.
        //   4. Restore vote-resolution wait for ALL slots. Today the resolver
        //      still expects N votes (TotalVotersExpected = SlotCount), so a
        //      per-player model wouldn't change the resolver — but you'd want
        //      to surface per-player aim/spawn positions in the network event
        //      so each client renders independently.
        //   5. Delete MergeAllRelicsForNavPhase / UnmergeNavPhaseRelics from
        //      CoopStateManager once nothing references them.
        // =====================================================================
        if (services.TryResolve<GameState.CoopStateManager>(out var stateMgr))
        {
            if (stateMgr.ActivePlayerSlot != 0)
            {
                stateMgr.SwapToPlayer(0);
            }

            stateMgr.MergeAllRelicsForNavPhase();
        }

        // Clear the host's turn banner — there are no turns during parallel-shoot.
        TurnChangeClientHandler.TurnMessage = string.Empty;

        if (services.TryResolve<IGameEventRegistry>(out var reg))
        {
            reg.Dispatch(new NavigatePhaseStartEvent
            {
                Source = source,
                ChildNodeCount = childNodeCount,
                ChildRoomTypes = childRoomTypes ?? System.Array.Empty<int>(),
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

    private static float _lastWatchdogLogAt;

    /// <summary>
    /// Host-only deadlock watchdog. Call from a periodic UI tick. Every 5s while
    /// the navigate phase is active but unresolved, logs the current tally,
    /// expected voter count, and which slot indices haven't voted yet — so a
    /// hung phase is visible in the host log instead of silently waiting forever.
    /// </summary>
    public static void TickWatchdog()
    {
        if (!CoopNavigateState.PhaseActive || CoopNavigateState.Resolved)
        {
            _lastWatchdogLogAt = 0f;
            return;
        }

        var services = MultiplayerPlugin.Services;
        if (services == null
            || !services.TryResolve<IMultiplayerMode>(out var mode)
            || !mode.IsHosting)
        {
            return;
        }

        var now = Time.unscaledTime;
        if (_lastWatchdogLogAt > 0f && now - _lastWatchdogLogAt < 5f)
        {
            return;
        }

        _lastWatchdogLogAt = now;
        var elapsed = CoopNavigateState.PhaseStartedAt > 0f
            ? now - CoopNavigateState.PhaseStartedAt
            : -1f;

        var pending = new List<int>();
        if (services.TryResolve<PlayerRegistry>(out var registry))
        {
            foreach (var slot in registry.GetAllSlots())
            {
                if (!CoopNavigateState.VotedSlots.Contains(slot.SlotIndex))
                {
                    pending.Add(slot.SlotIndex);
                }
            }
        }

        var pendingStr = pending.Count > 0 ? string.Join(",", pending) : "(none)";
        MultiplayerPlugin.Logger?.LogWarning(
            $"[CoopNavigate/Watchdog] Phase '{CoopNavigateState.Source}' unresolved after {elapsed:F1}s: " +
            $"votes={CoopNavigateState.VotedSlots.Count}/{CoopNavigateState.TotalVotersExpected}, " +
            $"tally=[{string.Join(",", CoopNavigateState.VoteCounts)}], " +
            $"pendingSlots={pendingStr}");
    }

    /// <summary>
    /// Host force-skip (FORCE_SKIP_SECONDS button). Resolves with whatever votes exist. If zero
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

        if (!forceSkip && !CoopNavigateState.AllVotesIn && !LeaderIsUncatchable())
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

        // Pop the relics we merged into slot 0 at StartPhase BEFORE the scene
        // transition — see the big comment in StartPhase for the design. This
        // restores slot 0's own relic loadout; non-host players keep theirs in
        // CoopPlayerState untouched (we never wrote to them during the phase).
        if (services?.TryResolve<GameState.CoopStateManager>(out var stateMgr) == true)
        {
            stateMgr.UnmergeNavPhaseRelics();
        }

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

    /// <summary>
    /// True when the current leader (unique max-vote child) cannot be caught
    /// or tied by the remaining voters. Lets the phase resolve early so
    /// players don't wait for a foregone-conclusion last vote.
    ///
    /// Rule: max >= secondMax + remainingVotes AND there is a unique leader.
    /// With "=", a leader who can at worst be tied still wins — matches
    /// the intent that the deciding-moment leader holds. If multiple
    /// children are tied at max, no early resolution (the tally itself
    /// hasn't decided anything).
    /// </summary>
    private static bool LeaderIsUncatchable()
    {
        var votes = CoopNavigateState.VoteCounts;
        if (votes == null || votes.Count == 0)
        {
            return false;
        }

        var remaining = CoopNavigateState.TotalVotersExpected - CoopNavigateState.VotedSlots.Count;
        if (remaining <= 0)
        {
            return false; // AllVotesIn handles this path
        }

        var max = -1;
        var secondMax = 0;
        var leaderUnique = false;
        for (var i = 0; i < votes.Count; i++)
        {
            if (votes[i] > max)
            {
                secondMax = max < 0 ? 0 : max;
                max = votes[i];
                leaderUnique = true;
            }
            else if (votes[i] == max)
            {
                leaderUnique = false;
            }
            else if (votes[i] > secondMax)
            {
                secondMax = votes[i];
            }
        }

        return leaderUnique && max >= secondMax + remaining;
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
