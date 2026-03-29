using System;
using System.Collections.Generic;
using System.Linq;
using Battle.Enemies;
using BepInEx.Logging;
using Data;
using HarmonyLib;
using Loading;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

/// <summary>
/// Syncs enemy state from host to client using GUID-based tracking.
/// 1. Match enemies by GUID (primary) or by locKey+position (fallback for first sync)
/// 2. Create missing enemies from prefab cache
/// 3. Destroy extra enemies not in host snapshot
/// 4. Register all enemies with host GUIDs in EnemyIdentifier
/// </summary>
public class EnemyStateApplier : IGameStateApplier<EnemyStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly EnemyIdentifier _enemyId;

    public EnemyStateApplier(ManualLogSource log, EnemyIdentifier enemyId)
    {
        _log = log;
        _enemyId = enemyId;
    }

    public void Apply(EnemyStateSnapshot snapshot)
    {
        try
        {
            if (snapshot.Enemies == null || snapshot.Enemies.Count == 0)
            {
                _log.LogInfo("[EnemyApplier] No enemies in snapshot.");
                return;
            }

            // Ensure enemy prefab cache is populated — BattleController.Awake may have
            // crashed or not loaded assets on the client
            EnsureEnemyPrefabsLoaded();

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

            _log.LogInfo($"[EnemyApplier] Applying {snapshot.Enemies.Count} host enemies to {liveEnemies.Count} client enemies");

            // Pass 1: match host enemies to client enemies by GUID first, then by name+position
            foreach (var entry in snapshot.Enemies)
            {
                _log.LogInfo($"[EnemyApplier] Host enemy: guid={entry.Id} loc={entry.LocKey} name={entry.EnemyName} " +
                    $"hp={entry.CurrentHealth}/{entry.MaxHealth} pos=({entry.PosX:F1},{entry.PosY:F1}) slot={entry.SlotIndex}");

                // Try GUID match first
                var match = FindByGuid(entry.Id);
                if (match != null && !matched.Contains(match))
                {
                    _log.LogInfo($"[EnemyApplier] GUID match: {entry.Id} → '{match.locKey}'");
                }
                else
                {
                    // Fallback to locKey + position match
                    match = FindBestMatch(liveEnemies, entry, matched);
                    if (match != null)
                    {
                        _log.LogInfo($"[EnemyApplier] Position match: '{match.locKey}' at ({match.transform.position.x:F1},{match.transform.position.y:F1}) → guid={entry.Id}");
                    }
                }

                if (match != null)
                {
                    // Force-update all state
                    match.CurrentHealth = entry.CurrentHealth;
                    SetMaxHealth(match, entry.MaxHealth);
                    match.transform.position = new Vector3(
                        entry.PosX, entry.PosY, match.transform.position.z);
                    matched.Add(match);
                    updated++;

                    // Register with host GUID
                    _enemyId.Register(match, entry.Id);
                }
                else
                {
                    // Try to create the enemy from the prefab cache
                    var spawned = TrySpawnEnemy(entry, em);
                    if (spawned != null)
                    {
                        created++;
                        // Register newly created enemy with host GUID
                        _enemyId.Register(spawned, entry.Id);
                    }
                    else
                    {
                        _log.LogWarning($"[EnemyApplier] Cannot spawn '{entry.EnemyName ?? entry.LocKey}' (guid={entry.Id}) — not in prefab cache");
                    }
                }
            }

            // Pass 2: destroy client enemies NOT in host snapshot
            foreach (var enemy in liveEnemies)
            {
                if (!matched.Contains(enemy))
                {
                    var guid = _enemyId.GetGuid(enemy) ?? "?";
                    _log.LogInfo($"[EnemyApplier] Destroying extra client enemy '{enemy.locKey}' (guid={guid})");
                    _enemyId.Unregister(enemy);
                    em.RemoveEnemy(enemy);
                    UnityEngine.Object.Destroy(enemy.gameObject);
                    destroyed++;
                }
            }

            _log.LogInfo($"[EnemyApplier] RESULT: Updated={updated}, Created={created}, Destroyed={destroyed} " +
                $"(host={snapshot.Enemies.Count}, client_before={liveEnemies.Count}, battle={snapshot.BattleStateName})");
            _enemyId.DumpState("AfterApply");
        }
        catch (Exception ex)
        {
            _log.LogError($"[EnemyApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Ensure the enemy prefab cache is populated. If BattleController.Awake didn't
    /// load them (e.g. it crashed during pegboard setup), load them ourselves from
    /// the current MapDataBattle's starterSpawns and waveGroups.
    /// </summary>
    private void EnsureEnemyPrefabsLoaded()
    {
        var cache = AssetLoading.Instance?.EnemyPrefabs;
        if (cache != null && cache.Count > 0) return;

        var battle = StaticGameData.dataToLoad as MapDataBattle;
        if (battle == null)
        {
            _log.LogWarning("[EnemyApplier] Cannot load enemy prefabs — no MapDataBattle in dataToLoad");
            return;
        }

        int loaded = 0;
        if (cache == null)
        {
            _log.LogWarning("[EnemyApplier] AssetLoading.Instance.EnemyPrefabs is null!");
            return;
        }

        // Load from starterSpawns
        if (battle.starterSpawns != null)
        {
            foreach (var spawn in battle.starterSpawns)
            {
                try
                {
                    if (spawn?.spawnData?.enemyAssetReference == null) continue;
                    var key = spawn.spawnData.enemyAssetReference.RuntimeKey.ToString();
                    if (cache.ContainsKey(key)) continue;
                    var go = spawn.spawnData.enemyAssetReference.LoadAssetAsync<GameObject>().WaitForCompletion();
                    if (go != null)
                    {
                        cache[key] = go;
                        loaded++;
                        _log.LogInfo($"[EnemyApplier] Loaded enemy prefab: key={key} name={go.name}");
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"[EnemyApplier] Failed to load starter spawn prefab: {ex.Message}");
                }
            }
        }

        // Load from wave groups
        if (battle.waveGroups != null)
        {
            foreach (var wg in battle.waveGroups)
            {
                if (wg?.waveData == null) continue;
                foreach (var wd in wg.waveData)
                {
                    try
                    {
                        if (wd?.spawnData?.enemyAssetReference == null) continue;
                        var key = wd.spawnData.enemyAssetReference.RuntimeKey.ToString();
                        if (cache.ContainsKey(key)) continue;
                        var go = wd.spawnData.enemyAssetReference.LoadAssetAsync<GameObject>().WaitForCompletion();
                        if (go != null)
                        {
                            cache[key] = go;
                            loaded++;
                            _log.LogInfo($"[EnemyApplier] Loaded wave enemy prefab: key={key} name={go.name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning($"[EnemyApplier] Failed to load wave prefab: {ex.Message}");
                    }
                }
            }
        }

        _log.LogInfo($"[EnemyApplier] Loaded {loaded} enemy prefabs from MapDataBattle (cache now has {cache.Count} entries)");
    }

    /// <summary>
    /// Look up an enemy by GUID from the registry.
    /// </summary>
    private Enemy FindByGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        return _enemyId.Find(guid);
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
    /// Returns the Enemy component if successful, null otherwise.
    /// Handles the case where EnemyManager isn't fully initialized (slots may be null).
    /// </summary>
    private Enemy TrySpawnEnemy(EnemyEntry entry, EnemyManager em)
    {
        try
        {
            var prefab = FindEnemyPrefab(entry.EnemyName ?? entry.LocKey);
            if (prefab == null) return null;

            var pos = new Vector3(entry.PosX, entry.PosY, 0);
            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity, em.transform);
            var enemy = go.GetComponentInChildren<Enemy>();
            if (enemy == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }

            enemy.CurrentHealth = entry.CurrentHealth;
            SetMaxHealth(enemy, entry.MaxHealth);

            // Try AddEnemy but don't crash if EnemyManager isn't initialized (slots null)
            try
            {
                em.AddEnemy(enemy, entry.SlotIndex, entry.IsFlying);
            }
            catch (Exception addEx)
            {
                _log.LogWarning($"[EnemyApplier] AddEnemy failed (EnemyManager may not be initialized): {addEx.Message}");
                // Still return the enemy — it's instantiated and positioned correctly
                // Just add it to the Enemies list directly if possible
                try
                {
                    var enemiesList = HarmonyLib.AccessTools.Field(typeof(EnemyManager), "Enemies")
                        ?.GetValue(em) as List<Enemy>;
                    if (enemiesList == null)
                    {
                        enemiesList = new List<Enemy>();
                        HarmonyLib.AccessTools.Field(typeof(EnemyManager), "Enemies")?.SetValue(em, enemiesList);
                    }
                    if (!enemiesList.Contains(enemy))
                        enemiesList.Add(enemy);
                }
                catch { }
            }

            _log.LogInfo($"[EnemyApplier] Spawned '{entry.EnemyName}' at ({entry.PosX:F1},{entry.PosY:F1}) slot={entry.SlotIndex} guid={entry.Id}");
            return enemy;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[EnemyApplier] TrySpawnEnemy failed for '{entry.EnemyName}': {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Search for an enemy prefab by name. Tries multiple strategies:
    /// 1. AssetLoading.Instance.EnemyPrefabs cache (values matched by name)
    /// 2. Resources.FindObjectsOfTypeAll for prefab GameObjects with Enemy component
    /// 3. Resources.FindObjectsOfTypeAll with partial name match
    /// </summary>
    private GameObject FindEnemyPrefab(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string cleanName = name.Replace("(Clone)", "").Trim();

        // Strategy 1: AssetLoading cache (keyed by RuntimeKey, so match by prefab name)
        var cache = AssetLoading.Instance?.EnemyPrefabs;
        if (cache != null && cache.Count > 0)
        {
            _log.LogInfo($"[EnemyApplier] Prefab cache has {cache.Count} entries, searching for '{cleanName}'");
            foreach (var kvp in cache)
            {
                if (kvp.Value != null && kvp.Value.name == cleanName)
                    return kvp.Value;
            }
            // Try partial match (enemy prefabs sometimes have variant names)
            foreach (var kvp in cache)
            {
                if (kvp.Value != null && (kvp.Value.name.Contains(cleanName) || cleanName.Contains(kvp.Value.name)))
                {
                    _log.LogInfo($"[EnemyApplier] Partial cache match: '{kvp.Value.name}' for '{cleanName}'");
                    return kvp.Value;
                }
            }
        }
        else
        {
            _log.LogWarning($"[EnemyApplier] Prefab cache {(cache == null ? "is null" : "has 0 entries")} — using Resources fallback");
        }

        // Strategy 2: search all loaded GameObjects with Enemy component (finds prefabs)
        var allEnemies = Resources.FindObjectsOfTypeAll<Battle.Enemies.Enemy>();
        foreach (var e in allEnemies)
        {
            if (e != null && e.gameObject.name == cleanName && e.gameObject.scene.name == null)
            {
                _log.LogInfo($"[EnemyApplier] Found prefab via Resources: '{cleanName}'");
                return e.gameObject;
            }
        }

        // Strategy 3: partial name match on prefabs
        foreach (var e in allEnemies)
        {
            if (e != null && e.gameObject.scene.name == null &&
                (e.gameObject.name.Contains(cleanName) || cleanName.Contains(e.gameObject.name)))
            {
                _log.LogInfo($"[EnemyApplier] Found prefab via partial Resources match: '{e.gameObject.name}' for '{cleanName}'");
                return e.gameObject;
            }
        }

        _log.LogWarning($"[EnemyApplier] No prefab found for '{cleanName}' (cache={cache?.Count ?? -1}, resources={allEnemies.Length})");
        return null;
    }

    private static void SetMaxHealth(Enemy enemy, float maxHealth)
    {
        if (maxHealth <= 0) return;
        var field = AccessTools.Field(typeof(Enemy), "_maxHealth");
        field?.SetValue(enemy, maxHealth);
    }
}
