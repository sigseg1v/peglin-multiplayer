using System;
using System.Collections.Generic;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Holds the in-flight state of the parallel-shoot end-of-stage navigate phase.
///
/// Flow:
///   Host enters PostBattleController.StartNavigation or NavOnlyController.PrepareForNavigation.
///   Host broadcasts NavigatePhaseStartEvent.
///   Each player (host + clients) arms a nav ball locally and shoots.
///   Each slot-hit becomes a vote: clients via NavigateVoteEvent, host via direct call to RecordVote.
///   Host tallies; broadcasts NavigateVoteUpdateEvent on every vote.
///   When all expected voters voted (or FORCE_SKIP_SECONDS force-skip), host picks winner (max votes;
///   ties -> Random.Range), broadcasts NavigateResolvedEvent, then calls native nav flow.
/// </summary>
public static class CoopNavigateState
{
    /// <summary>True on host or client when the parallel navigate phase is active.</summary>
    public static bool PhaseActive;

    /// <summary>"post_battle" or "nav_only" — drives which native flow is used to resolve.</summary>
    public static string Source;

    /// <summary>Number of available child node choices for this navigate (1..3).</summary>
    public static int ChildNodeCount;

    /// <summary>Per-child RoomType ints (Worldmap.RoomType). Length == ChildNodeCount. Drives client slot icon/tint.</summary>
    public static int[] ChildRoomTypes = System.Array.Empty<int>();

    /// <summary>Current tally indexed by child index. Length == ChildNodeCount.</summary>
    public static List<int> VoteCounts = new List<int>();

    /// <summary>Slot indices that have voted (host = 0). Used to short-circuit duplicate votes.</summary>
    public static HashSet<int> VotedSlots = new HashSet<int>();

    /// <summary>Number of players expected to vote (host included). Decremented on disconnect.</summary>
    public static int TotalVotersExpected;

    /// <summary>True once the host has committed a winning index. Late votes are ignored.</summary>
    public static bool Resolved;

    /// <summary>Final winning child index after Resolved=true. Used by client UI.</summary>
    public static int ChosenChildIndex = -1;

    /// <summary>Client-side: true after this client has cast its vote (used to suppress further votes).</summary>
    public static bool LocalVoteCast;

    /// <summary>Time (Time.unscaledTime) when the phase started; for the FORCE_SKIP_SECONDS host force-skip.</summary>
    public static float PhaseStartedAt = -1f;

    public static bool AllVotesIn => TotalVotersExpected > 0 && VotedSlots.Count >= TotalVotersExpected;

    public static void StartPhase(string source, int childNodeCount, int totalVoters, float now, int[] childRoomTypes = null)
    {
        // Destroy any ghost balls from a prior phase that did not get cleaned up
        // (for example if the previous phase resolved before Reset ran).
        NavBallShotClientHandler.DestroyAllGhostBalls();
        NavigatePhaseStartClientHandler.DestroyActiveVerifier();

        PhaseActive = true;
        Source = source ?? "post_battle";
        ChildNodeCount = Math.Max(1, childNodeCount);
        TotalVotersExpected = Math.Max(1, totalVoters);
        VoteCounts = new List<int>(new int[ChildNodeCount]);
        VotedSlots = new HashSet<int>();
        Resolved = false;
        ChosenChildIndex = -1;
        LocalVoteCast = false;
        PhaseStartedAt = now;
        ChildRoomTypes = childRoomTypes ?? System.Array.Empty<int>();
        Patches.PachinkoBallPatches.ResetNavBallBroadcastLatch();
    }

    /// <summary>Records a vote, returns true if the vote was accepted (slot wasn't already voted).</summary>
    public static bool RecordVote(int slotIndex, int childIndex)
    {
        if (!PhaseActive || Resolved)
        {
            return false;
        }

        if (childIndex < 0 || childIndex >= VoteCounts.Count)
        {
            return false;
        }

        if (!VotedSlots.Add(slotIndex))
        {
            return false;
        }

        VoteCounts[childIndex] = VoteCounts[childIndex] + 1;
        return true;
    }

    public static void Reset()
    {
        NavBallShotClientHandler.DestroyAllGhostBalls();
        NavigatePhaseStartClientHandler.DestroyActiveVerifier();

        PhaseActive = false;
        Source = null;
        ChildNodeCount = 0;
        VoteCounts = new List<int>();
        VotedSlots = new HashSet<int>();
        TotalVotersExpected = 0;
        Resolved = false;
        ChosenChildIndex = -1;
        LocalVoteCast = false;
        PhaseStartedAt = -1f;
        ChildRoomTypes = System.Array.Empty<int>();
    }
}
