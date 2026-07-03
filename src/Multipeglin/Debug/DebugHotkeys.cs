using System;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Debug;

/// <summary>
/// Playtest-only hotkeys, gated by the MULTIPEGLIN_DEBUG env var (set to "1"
/// before launching the game). Currently:
///   F10 — host only: deal 99,999 damage to every active enemy.
///   F9  — host only: detonate every bomb on the board.
/// </summary>
public sealed class DebugHotkeys : MonoBehaviour
{
    private const string EnvVar = "MULTIPEGLIN_DEBUG";
    private bool _enabled;

    private void Awake()
    {
        var v = Environment.GetEnvironmentVariable(EnvVar);
        _enabled = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        if (_enabled)
        {
            MultiplayerPlugin.Logger?.LogInfo($"[DebugHotkeys] enabled via {EnvVar}; F10 = nuke enemies, F9 = detonate bombs");
        }
    }

    private void Update()
    {
        if (!_enabled)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            DetonateAllBombs();
            return;
        }

        if (!Input.GetKeyDown(KeyCode.F10))
        {
            return;
        }

        if (!IsHost("F10"))
        {
            return;
        }

        var em = UnityEngine.Object.FindObjectOfType<EnemyManager>();
        if (em == null || em.Enemies == null || em.Enemies.Count == 0)
        {
            MultiplayerPlugin.Logger?.LogInfo("[DebugHotkeys] F10 ignored — no EnemyManager/enemies");
            return;
        }

        var snapshot = new System.Collections.Generic.List<global::Battle.Enemies.Enemy>(em.Enemies);
        var killed = 0;
        foreach (var enemy in snapshot)
        {
            if (enemy == null)
            {
                continue;
            }

            try
            {
                enemy.Damage(
                    99999L,
                    screenshake: false,
                    audioScale: 0f,
                    damageMod: 1f,
                    unblockable: true,
                    damageSource: global::Battle.Enemies.Enemy.EnemyDamageSource.Unspecified,
                    sourceIsPlayer: true,
                    dealMaxHPDamage: true);
                killed++;
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[DebugHotkeys] Damage failed on enemy: {ex.Message}");
            }
        }

        MultiplayerPlugin.Logger?.LogInfo($"[DebugHotkeys] F10 nuked {killed} enemies");
    }

    private static bool IsHost(string key)
    {
        if (MultiplayerPlugin.Services == null
            || !MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)
            || !mode.IsHosting)
        {
            MultiplayerPlugin.Logger?.LogInfo($"[DebugHotkeys] {key} ignored — not hosting");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Detonate every live bomb on the board via the native Bomb.PegActivated
    /// path (HitCount > 1 triggers detonation), so nav gold, splash relics and
    /// the Peg.OnPegActivated sync hooks all fire exactly as if the player hit
    /// each bomb twice.
    /// </summary>
    private void DetonateAllBombs()
    {
        if (!IsHost("F9"))
        {
            return;
        }

        var bombs = UnityEngine.Object.FindObjectsOfType<global::Bomb>();
        if (bombs == null || bombs.Length == 0)
        {
            MultiplayerPlugin.Logger?.LogInfo("[DebugHotkeys] F9 ignored — no bombs on board");
            return;
        }

        var detonated = 0;
        foreach (var bomb in bombs)
        {
            if (bomb == null || bomb.detonated)
            {
                continue;
            }

            try
            {
                var guard = 0;
                while (!bomb.detonated && guard++ < 3)
                {
                    bomb.PegActivated(playAudio: false);
                }

                if (bomb.detonated)
                {
                    detonated++;
                }
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[DebugHotkeys] Detonate failed on bomb: {ex.Message}");
            }
        }

        MultiplayerPlugin.Logger?.LogInfo($"[DebugHotkeys] F9 detonated {detonated}/{bombs.Length} bombs");
    }
}
