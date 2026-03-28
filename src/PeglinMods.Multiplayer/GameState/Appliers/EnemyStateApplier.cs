using System;
using System.Linq;
using Battle.Enemies;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

public class EnemyStateApplier : IGameStateApplier<EnemyStateSnapshot>
{
    private readonly ManualLogSource _log;

    public EnemyStateApplier(ManualLogSource log) => _log = log;

    public void Apply(EnemyStateSnapshot snapshot)
    {
        try
        {
            var enemyManager = UnityEngine.Object.FindObjectOfType<EnemyManager>();
            if (enemyManager == null)
            {
                _log.LogInfo($"[EnemyApplier] No EnemyManager in scene. Battle state: {snapshot.BattleStateName}, enemies: {snapshot.Enemies?.Count ?? 0}");
                return;
            }

            var liveEnemies = enemyManager.Enemies;
            if (liveEnemies == null)
            {
                _log.LogWarning("[EnemyApplier] EnemyManager.Enemies list is null.");
                return;
            }

            int matched = 0;
            int unmatched = 0;

            foreach (var entry in snapshot.Enemies)
            {
                var liveEnemy = FindMatchingEnemy(liveEnemies, entry);
                if (liveEnemy == null)
                {
                    _log.LogWarning($"[EnemyApplier] No match for enemy '{entry.LocKey}' slot={entry.SlotIndex}");
                    unmatched++;
                    continue;
                }

                liveEnemy.CurrentHealth = entry.CurrentHealth;
                liveEnemy.transform.position = new Vector3(entry.PosX, entry.PosY, liveEnemy.transform.position.z);
                matched++;

                if (entry.StatusEffects != null && entry.StatusEffects.Count > 0)
                {
                    _log.LogInfo($"[EnemyApplier] Enemy '{entry.LocKey}' has {entry.StatusEffects.Count} status effects (log only)");
                }
            }

            _log.LogInfo($"[EnemyApplier] Applied: {matched} matched, {unmatched} unmatched, round={snapshot.RoundCount}, battleState={snapshot.BattleStateName}");
        }
        catch (Exception ex)
        {
            _log.LogError($"[EnemyApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static Enemy FindMatchingEnemy(System.Collections.Generic.List<Enemy> liveEnemies, EnemyEntry entry)
    {
        // Try to match by locKey first, then narrow by slot index if multiple
        var candidates = liveEnemies.Where(e => e != null && e.locKey == entry.LocKey).ToList();

        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count > 1)
        {
            // Multiple enemies with same locKey - pick closest by position
            return candidates.OrderBy(e =>
            {
                var pos = e.transform.position;
                float dx = pos.x - entry.PosX;
                float dy = pos.y - entry.PosY;
                return dx * dx + dy * dy;
            }).First();
        }

        // No locKey match - try closest by position as last resort
        if (liveEnemies.Count > 0)
        {
            var closest = liveEnemies
                .Where(e => e != null)
                .OrderBy(e =>
                {
                    var pos = e.transform.position;
                    float dx = pos.x - entry.PosX;
                    float dy = pos.y - entry.PosY;
                    return dx * dx + dy * dy;
                }).FirstOrDefault();

            if (closest != null)
            {
                var pos = closest.transform.position;
                float dist = Mathf.Sqrt((pos.x - entry.PosX) * (pos.x - entry.PosX) + (pos.y - entry.PosY) * (pos.y - entry.PosY));
                if (dist < 2f) // Only accept if reasonably close
                    return closest;
            }
        }

        return null;
    }
}
