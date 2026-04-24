namespace Multipeglin.Events.Handlers.Battle;

using System;
using System.Collections;
using System.Collections.Generic;
using global::Battle;
using global::Battle.Attacks;
using Multipeglin.Events.Network.Battle;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using UnityEngine;

public sealed class AttackStartedClientHandler : IClientHandler<AttackStartedEvent>
{
    // Per-shot events arrive back-to-back during a coop attack phase. We queue
    // them and play one at a time on ClientAttackProjectile so each player's
    // orb completes its flight before the next begins.
    private static readonly Queue<AttackStartedEvent> _queue = new Queue<AttackStartedEvent>();
    private static bool _playbackRunning;

    public void Handle(AttackStartedEvent e)
    {
        try
        {
            _queue.Enqueue(e);
            StartPlaybackIfNeeded();
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"AttackStarted handler failed: {ex.Message}");
        }
    }

    private static void StartPlaybackIfNeeded()
    {
        if (_playbackRunning) return;
        var runner = ClientAttackProjectile.Instance;
        if (runner == null)
        {
            // No MonoBehaviour available to host the coroutine — fall back to
            // one-shot handling so we don't lose the event entirely.
            if (_queue.Count > 0) PlayOneImmediate(_queue.Dequeue());
            return;
        }
        _playbackRunning = true;
        runner.StartCoroutine(Playback());
    }

    private static IEnumerator Playback()
    {
        while (_queue.Count > 0)
        {
            var e = _queue.Dequeue();
            PlayOneImmediate(e);

            // Wait for this shot's projectile to finish before starting the next.
            float waited = 0f;
            var cap = ClientAttackProjectile.Instance;
            while (cap != null && cap.IsAttacking && waited < 2.0f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            // Small gap between shots so enemy flinch animations are legible.
            yield return new WaitForSeconds(0.3f);
        }
        _playbackRunning = false;
    }

    private static void PlayOneImmediate(AttackStartedEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;

            // Fire BattleController.OnAttackStarted for each slot so any local UI
            // (orb scale-out, peglin animator on non-spectating host) updates per shot.
            BattleController.OnAttackStarted?.Invoke();

            if (mode != null && mode.IsSpectating && !string.IsNullOrEmpty(e.AnimTrigger))
            {
                AttackManager.OnAttackPerformed?.Invoke(e.AnimTrigger);
                ClientAttackProjectile.Instance?.SetupAttack(e.TargetEnemyGuid, e.NumPegsHit, e.IsCrit, e.OrbName);

                MultiplayerPlugin.Logger?.LogInfo(
                    $"[AttackStarted] slot={e.SlotIndex} anim='{e.AnimTrigger}' target={e.TargetEnemyGuid} " +
                    $"pegs={e.NumPegsHit} crit={e.IsCrit} orb={e.OrbName}");
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"AttackStarted playback failed: {ex.Message}");
        }
    }
}
