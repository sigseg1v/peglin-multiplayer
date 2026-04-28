using System.Text;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Periodic deadlock detector. While the local view is showing a "waiting
/// for other players" overlay, logs a snapshot of every relevant flag every
/// 5s. If the same snapshot recurs unchanged for &gt;15s, escalates to an
/// error so a soft-locked lockstep is impossible to miss in the log.
/// </summary>
public static class CoopWaitWatchdog
{
    private static float _lastLogAt;
    private static string _lastSnapshot;
    private static float _snapshotStableSince;

    public static void Tick(IMultiplayerMode mode)
    {
        if (mode == null || (!mode.IsHosting && !mode.IsSpectating))
        {
            Reset();
            return;
        }

        var waiting = CoopRewardState.WaitingForOtherPlayers
            || (CoopNavigateState.PhaseActive && !CoopNavigateState.Resolved);

        if (!waiting)
        {
            Reset();
            return;
        }

        var now = Time.unscaledTime;
        if (_lastLogAt > 0f && now - _lastLogAt < 5f)
        {
            return;
        }

        _lastLogAt = now;
        var snapshot = BuildSnapshot();

        if (snapshot == _lastSnapshot)
        {
            var stuckFor = now - _snapshotStableSince;
            if (stuckFor >= 15f)
            {
                MultiplayerPlugin.Logger?.LogError(
                    $"[CoopWaitWatchdog] DEADLOCK SUSPECTED — same wait state for {stuckFor:F1}s: {snapshot}");
            }
            else
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopWaitWatchdog] still waiting ({stuckFor:F1}s): {snapshot}");
            }
        }
        else
        {
            _lastSnapshot = snapshot;
            _snapshotStableSince = now;
            MultiplayerPlugin.Logger?.LogInfo($"[CoopWaitWatchdog] wait state: {snapshot}");
        }
    }

    private static void Reset()
    {
        _lastLogAt = 0f;
        _lastSnapshot = null;
        _snapshotStableSince = 0f;
    }

    private static string BuildSnapshot()
    {
        var sb = new StringBuilder(256);
        sb.Append("WaitForOthers=").Append(CoopRewardState.WaitingForOtherPlayers);
        sb.Append(" AllChoicesComplete=").Append(CoopRewardState.AllChoicesComplete);
        sb.Append(" HostRewardActive=").Append(CoopRewardState.HostRewardPhaseActive);
        sb.Append(" HostRewardsDone=").Append(CoopRewardState.HostRewardsDone);
        sb.Append(" ClientInNativeReward=").Append(CoopRewardState.ClientInNativeRewardPhase);
        sb.Append(" RewardClients=")
            .Append(CoopRewardState.ClientRewardChoicesReceived?.Count ?? 0)
            .Append('/').Append(CoopRewardState.TotalRewardClientsExpected);
        sb.Append(" Nav.PhaseActive=").Append(CoopNavigateState.PhaseActive);
        sb.Append(" Nav.LocalVoteCast=").Append(CoopNavigateState.LocalVoteCast);
        sb.Append(" Nav.Resolved=").Append(CoopNavigateState.Resolved);
        sb.Append(" Nav.Votes=").Append(CoopNavigateState.VotedSlots?.Count ?? 0)
            .Append('/').Append(CoopNavigateState.TotalVotersExpected);
        return sb.ToString();
    }
}
