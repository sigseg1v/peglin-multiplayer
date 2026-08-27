namespace Multipeglin.Events.Network.Battle;

/// <summary>
/// Absolute host value of BattleController._criticalHitCount, broadcast whenever
/// it changes. The delegate-driven CritActivatedEvent / CritDeactivatedEvent only
/// cover the two sites that actually invoke onCriticalHitActivated /
/// onCriticalHitDeactivated; four of the five sites that clear the counter do it
/// silently, so the client used to keep a red board until the next 2 s pegboard
/// heartbeat corrected it ([CritSync] 1 -> 0 in the client log).
/// </summary>
public class CritCountEvent
{
    public int Count { get; set; }
}
