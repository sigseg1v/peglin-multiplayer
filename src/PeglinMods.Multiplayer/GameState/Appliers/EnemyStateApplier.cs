using System;
using System.Collections.Generic;
using System.Linq;
using Battle.Enemies;
using BepInEx.Logging;
using HarmonyLib;
using Loading;
using PeglinMods.Multiplayer.GameState.Snapshots;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

/// <summary>
/// Aggressively syncs enemy state from host to client.
/// Destroys enemies that don't exist on the host.
/// Creates enemies that exist on the host but not on the client.
/// Force-updates health, position, and max health on matched enemies.
/// </summary>
public class EnemyStateApplier : IGameStateApplier<EnemyStateSnapshot>
{
    private readonly ManualLogSource _log;

    public EnemyStateApplier(ManualLogSource log) => _log = log;

    public void Apply(EnemyStateSnapshot snapshot)
    {
        try
        {
            if (snapshot.Enemies == null || snapshot.Enemies.Count == 0)
            {
                _log.LogInfo("[EnemyApplier] No enemies in snapshot.");
                return;
            }

            var em = UnityEngine.Object.FindObjectOfType<EnemyManager>();
            if (em == null)
            {
                _log.LogInfo($"[EnemyApplier] No EnemyManager in scene (battle={snapshot.BattleStateName})");
                return;
            }

            var liveEnemies = em.Enemies?.Where(e => e != null).ToList()
                ?? new List<Enemy>();
            var matched = new HashSet<Enemy>();
            int updated = 0, created = 0, destroyed = 0;

            // Pass 1: match host enemies to client enemies, update matched ones
            foreach (var entry in snapshot.Enemies)
            {
                var match = FindBestMatch(liveEnemies, entry, matched);
                if (match != null)
                {
                    // Force-update all state
                    match.CurrentHealth = entry.CurrentHealth;
                    SetMaxHealth(match, entry.MaxHealth);
                    match.transform.position = new Vector3(
                        entry.PosX, entry.PosY, match.transform.position.z);
                    matched.Add(match);
                    updated++;
                }
                else
                {
                    // Try to create the enemy from the prefab cache
                    if (TrySpawnEnemy(entry, em))
                        created++;
                    else
                        _log.LogWarning($"[EnemyApplier] Cannot spawn '{entry.EnemyName ?? entry.LocKey}' — not in prefab cache");
                }
            }

            // Pass 2: destroy client enemies NOT in host snapshot
            foreach (var enemy in liveEnemies)
            {
                if (!matched.Contains(enemy))
                {
                    _log.LogInfo($"[EnemyApplier] Destroying extra client enemy '{enemy.locKey}'");
                    em.RemoveEnemy(enemy);
                    UnityEngine.Object.Destroy(enemy.gameObject);
                    destroyed++;
                }
            }

            _log.LogInfo($"[EnemyApplier] Updated={updated}, Created={created}, Destroyed={destroyed} " +
                $"(host={snapshot.Enemies.Count}, client_before={liveEnemies.Count}, battle={snapshot.BattleStateName})");
        }
        catch (Exception ex)
        {
            _log.LogError($"[EnemyApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Find the best matching client enemy for a host enemy entry.
    /// Matches by locKey first, then by closest position. Avoids double-matching.
    /// </summary>
    private static Enemy FindBestMatch(List<Enemy> liveEnemies, EnemyEntry entry, HashSet<Enemy> alreadyMatched)
    {
        // Prefer locKey match, then closest position
        Enemy best = null;
        float bestDist = float.MaxValue;

        foreach (var e in liveEnemies)
        {
            if (e == null || alreadyMatched.Contains(e)) continue;
            if (e.locKey != entry.LocKey) continue;

            float dx = e.transform.position.x - entry.PosX;
            float dy = e.transform.position.y - entry.PosY;
            float dist = dx * dx + dy * dy;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = e;
            }
        }

        if (best != null) return best;

        // Fallback: any enemy within 3 units (regardless of locKey)
        foreach (var e in liveEnemies)
        {
            if (e == null || alreadyMatched.Contains(e)) continue;
            float dx = e.transform.position.x - entry.PosX;
            float dy = e.transform.position.y - entry.PosY;
            float dist = dx * dx + dy * dy;
            if (dist < 9f && dist < bestDist) // 3 units
            {
                bestDist = dist;
                best = e;
            }
        }

        return best;
    }

    /// <summary>
    /// Try to instantiate an enemy from the game's asset cache.
    /// </summary>
    private bool TrySpawnEnemy(EnemyEntry entry, EnemyManager em)
    {
        try
        {
            var prefab = FindEnemyPrefab(entry.EnemyName ?? entry.LocKey);
            if (prefab == null) return false;

            var pos = new Vector3(entry.PosX, entry.PosY, 0);
            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity, em.transform);
            var enemy = go.GetComponentInChildren<Enemy>();
            if (enemy == null)
            {
                UnityEngine.Object.Destroy(go);
                return false;
            }

            enemy.CurrentHealth = entry.CurrentHealth;
            SetMaxHealth(enemy, entry.MaxHealth);
            em.AddEnemy(enemy, entry.SlotIndex, entry.IsFlying);

            _log.LogInfo($"[EnemyApplier] Spawned '{entry.EnemyName}' at ({entry.PosX:F1},{entry.PosY:F1}) slot={entry.SlotIndex}");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[EnemyApplier] TrySpawnEnemy failed for '{entry.EnemyName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Search for an enemy prefab by name. Tries multiple strategies:
    /// 1. AssetLoading.Instance.EnemyPrefabs cache (values matched by name)
    /// 2. Resources.FindObjectsOfTypeAll as fallback
    /// </summary>
    private GameObject FindEnemyPrefab(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string cleanName = name.Replace("(Clone)", "").Trim();

        // Strategy 1: AssetLoading cache
        var cache = AssetLoading.Instance?.EnemyPrefabs;
        if (cache != null)
        {
            _log.LogInfo($"[EnemyApplier] Prefab cache has {cache.Count} entries, searching for '{cleanName}'");
            foreach (var kvp in cache)
            {
                if (kvp.Value != null && kvp.Value.name == cleanName)
                    return kvp.Value;
            }
            // Log what IS in the cache for debugging
            if (cache.Count <= 20)
            {
                foreach (var kvp in cache)
                    _log.LogInfo($"[EnemyApplier]   cache: key={kvp.Key}, name={kvp.Value?.name}");
            }
        }
        else
        {
            _log.LogWarning("[EnemyApplier] AssetLoading.Instance or EnemyPrefabs is null");
        }

        // Strategy 2: search all loaded GameObjects with Enemy component
        var allEnemies = Resources.FindObjectsOfTypeAll<Battle.Enemies.Enemy>();
        foreach (var e in allEnemies)
        {
            if (e != null && e.gameObject.name == cleanName && e.gameObject.scene.name == null)
            {
                // scene.name == null means it's a prefab (not instantiated in a scene)
                _log.LogInfo($"[EnemyApplier] Found prefab via Resources: '{cleanName}'");
                return e.gameObject;
            }
        }

        return null;
    }

    private static void SetMaxHealth(Enemy enemy, float maxHealth)
    {
        if (maxHealth <= 0) return;
        var field = AccessTools.Field(typeof(Enemy), "_maxHealth");
        field?.SetValue(enemy, maxHealth);
    }
}
