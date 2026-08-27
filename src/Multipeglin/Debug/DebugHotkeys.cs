using System;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Debug;

/// <summary>
/// Playtest-only hotkeys, gated by the MULTIPEGLIN_DEBUG env var (set to "1"
/// before launching the game). Currently:
///   F10 — host only: deal 99,999 damage to every active enemy.
///   F9  — host only: detonate every bomb on the board.
///   F8  — either side: dump the pegboard hierarchy (parent chain histogram)
///         so host and client structure can be diffed from the two logs.
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
            MultiplayerPlugin.Logger?.LogInfo($"[DebugHotkeys] enabled via {EnvVar}; F10 = nuke enemies, F9 = detonate bombs, F8 = dump pegboard");
        }
    }

    private void Update()
    {
        if (!_enabled)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.F8))
        {
            DumpPegboard();
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

    /// <summary>
    /// Log every peg in the gameplay scene grouped by ancestor chain. Long pegs
    /// share a placeholder transform, so the collider centre is logged too —
    /// that is the only value that identifies them.
    /// </summary>
    private void DumpPegboard()
    {
        var pegs = UnityEngine.Object.FindObjectsOfType<Peg>(includeInactive: true);
        var byParent = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.List<Peg>>(StringComparer.Ordinal);
        var total = 0;
        foreach (var peg in pegs)
        {
            if (peg == null || peg.gameObject.scene.name == "Prediction")
            {
                continue;
            }

            total++;
            var chain = HierarchyPath(peg.transform.parent);
            if (!byParent.TryGetValue(chain, out var list))
            {
                list = new System.Collections.Generic.List<Peg>();
                byParent[chain] = list;
            }

            list.Add(peg);
        }

        MultiplayerPlugin.Logger?.LogInfo($"[PegDump] {total} scene pegs in {byParent.Count} parent groups");
        foreach (var kv in byParent)
        {
            var longPegs = 0;
            var inactive = 0;
            foreach (var peg in kv.Value)
            {
                if (peg is LongPeg)
                {
                    longPegs++;
                }

                if (!peg.gameObject.activeInHierarchy)
                {
                    inactive++;
                }
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[PegDump]   {kv.Key} → {kv.Value.Count} pegs (long={longPegs}, inactive={inactive})");

            foreach (var peg in kv.Value)
            {
                if (!(peg is LongPeg))
                {
                    continue;
                }

                Vector3 centre;
                try
                {
                    centre = Multipeglin.Utility.LongPegVisualHelper.WorldCenter(peg);
                }
                catch
                {
                    centre = Vector3.zero;
                }

                MultiplayerPlugin.Logger?.LogInfo(
                    $"[PegDump]     long #{peg.transform.GetSiblingIndex()} {peg.gameObject.name} " +
                    $"centre=({centre.x:F2},{centre.y:F2}) pos=({peg.transform.position.x:F2},{peg.transform.position.y:F2}) " +
                    $"active={peg.gameObject.activeInHierarchy}");
            }
        }
    }

    private static string HierarchyPath(Transform t)
    {
        if (t == null)
        {
            return "<root>";
        }

        var parts = new System.Collections.Generic.List<string>(8);
        for (var cur = t; cur != null; cur = cur.parent)
        {
            parts.Add(cur.name);
        }

        parts.Reverse();
        return string.Join("/", parts.ToArray());
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
        var liveBombs = 0;
        foreach (var bomb in bombs)
        {
            if (bomb == null || bomb.detonated)
            {
                continue;
            }

            // PredictionManager clones the complete pegboard into its own
            // local-physics scene. Those dummy Bomb components are active and
            // therefore included by FindObjectsOfType, but they are not wired
            // to the live battle delegates/relic state and PegActivated throws.
            // Only drive bombs from the gameplay scene.
            if (bomb.gameObject.scene.name == "Prediction")
            {
                continue;
            }

            liveBombs++;

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

        MultiplayerPlugin.Logger?.LogInfo($"[DebugHotkeys] F9 detonated {detonated}/{liveBombs} live bombs");
    }
}
