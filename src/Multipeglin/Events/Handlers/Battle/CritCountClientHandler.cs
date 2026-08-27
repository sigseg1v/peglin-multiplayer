using System;
using System.Reflection;
using global::Battle;
using HarmonyLib;
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

/// <summary>
/// Mirrors the host's absolute crit counter and repaints the board on the
/// transitions that change BattleController.criticalActive.
///
/// This is the low-latency twin of PegboardStateApplier.ApplyCriticalHitCount:
/// same value, same source of truth, ~1 frame instead of up to one 2 s heartbeat.
/// </summary>
public sealed class CritCountClientHandler : IClientHandler<CritCountEvent>
{
    private static readonly FieldInfo CritCountField
        = AccessTools.Field(typeof(BattleController), "_criticalHitCount");

    public void Handle(CritCountEvent networkEvent)
    {
        if (CritCountField == null)
        {
            return;
        }

        try
        {
            var current = (int)(CritCountField.GetValue(null) ?? 0);
            if (current == networkEvent.Count)
            {
                return;
            }

            CritCountField.SetValue(null, networkEvent.Count);

            // criticalActive is `_criticalHitCount > 0`, so only the 0 <-> non-zero
            // transitions change peg sprites. Stacking crits (1 -> 2) needs no repaint.
            MultiplayerPlugin.Logger?.LogInfo($"[CritSync] host count {current} -> {networkEvent.Count}");

            if (current == 0 && networkEvent.Count > 0)
            {
                BattleController.onCriticalHitActivated?.Invoke();
            }
            else if (networkEvent.Count == 0)
            {
                BattleController.onCriticalHitDeactivated?.Invoke();
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"CritCount handler failed: {e.Message}");
        }
    }
}
