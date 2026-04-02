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
            // Show waiting message when host is in post-battle state
            if (snapshot.BattleStateName == "AWAITING_POST_BATTLE_CONTROLLER")
            {
                MapStateApplier.ClientWaitingMessage = "Host is choosing end-of-battle rewards...";
            }

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
                    // Check if the enemy type changed (e.g. Stump → StumpDead)
                    // If the name doesn't match, destroy the old and spawn the new
                    string matchName = match.gameObject.name.Replace("(Clone)", "").Trim();
                    string hostName = (entry.EnemyName ?? "").Replace("(Clone)", "").Trim();
                    if (!string.IsNullOrEmpty(hostName) && matchName != hostName)
                    {
                        _log.LogInfo($"[EnemyApplier] Enemy type changed: '{matchName}' → '{hostName}', replacing");
                        _enemyId.Unregister(match);
                        em.RemoveEnemy(match);
                        UnityEngine.Object.Destroy(match.gameObject);
                        matched.Add(match); // mark old as handled
                        destroyed++;

                        // Spawn the new enemy type
                        var spawned = TrySpawnEnemy(entry, em);
                        if (spawned != null)
                        {
                            created++;
                            _enemyId.Register(spawned, entry.Id);
                            SyncStatusEffects(spawned, entry);
                        }
                    }
                    else
                    {
                        // Same type — update state
                        SetMaxHealth(match, entry.MaxHealth);
                        match.CurrentHealth = entry.CurrentHealth;
                        // Set position directly — enemy animations handle the visual movement
                        match.transform.position = new Vector3(
                            entry.PosX, entry.PosY, match.transform.position.z);
                        ForceUpdateHealthBar(match);
                        SyncStatusEffects(match, entry);
                        matched.Add(match);
                        updated++;
                        _enemyId.Register(match, entry.Id);
                    }
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
                        SyncStatusEffects(spawned, entry);
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

            // Sync upcoming enemy preview from host's actual list
            SyncUpcomingEnemies(snapshot.UpcomingEnemyNames);
        }
        catch (Exception ex)
        {
            _log.LogError($"[EnemyApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Rebuild the upcoming enemy preview UI from host data.
    /// Destroys all existing UI elements and recreates from host's enemy name list
    /// using the prefab cache. This runs every sync so the client always mirrors the host.
    /// </summary>
    private List<string> _lastSyncedUpcoming;

    private void SyncUpcomingEnemies(System.Collections.Generic.List<string> hostNames)
    {
        try
        {
            var eim = UnityEngine.Object.FindObjectOfType<Battle.EnemyInfoManager>();
            if (eim == null) return;

            var elementsField = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_enemyInfoElements");
            var elements = elementsField?.GetValue(eim) as System.Collections.Generic.Queue<Battle.EnemyInfoElement>;
            if (elements == null) return;

            int hostCount = hostNames?.Count ?? 0;

            // Skip rebuild if the list hasn't changed (avoids flicker from constant destroy+recreate)
            if (_lastSyncedUpcoming != null && hostNames != null &&
                _lastSyncedUpcoming.Count == hostNames.Count &&
                _lastSyncedUpcoming.SequenceEqual(hostNames))
                return;

            // Destroy all existing UI elements
            int oldCount = elements.Count;
            while (elements.Count > 0)
            {
                var el = elements.Dequeue();
                if (el != null) UnityEngine.Object.Destroy(el.gameObject);
            }

            // Clear the backing _upcomingSpawns list too
            var upcomingField = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_upcomingSpawns");
            var upcomingList = upcomingField?.GetValue(eim) as System.Collections.IList;
            upcomingList?.Clear();

            if (hostNames == null || hostNames.Count == 0)
            {
                // Hide the scroll when no upcoming enemies
                try
                {
                    var scrollAnim = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_scrollAnim")?.GetValue(eim) as UnityEngine.Animator;
                    scrollAnim?.SetTrigger(UnityEngine.Animator.StringToHash("RollUp"));
                }
                catch { }
                if (oldCount > 0)
                    _log.LogInfo($"[EnemyApplier] Upcoming sync: cleared {oldCount} → 0");
                return;
            }

            // Rebuild UI from host enemy names using prefab cache
            var prefabs = Loading.AssetLoading.Instance?.EnemyPrefabs;
            var containerField = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_enemyInfoContainer");
            var container = containerField?.GetValue(eim) as UnityEngine.Transform;
            var prefabField = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_enemyInfoElementPrefab");
            var elementPrefab = prefabField?.GetValue(eim) as Battle.EnemyInfoElement;

            if (prefabs == null || container == null || elementPrefab == null) return;

            int created = 0;
            foreach (var name in hostNames)
            {
                // Find the enemy prefab by name — try exact match then substring
                UnityEngine.GameObject prefabGo = null;
                foreach (var kvp in prefabs)
                {
                    if (kvp.Value != null && kvp.Value.name == name)
                    {
                        prefabGo = kvp.Value;
                        break;
                    }
                }
                if (prefabGo == null)
                {
                    // Try partial match (prefab key often differs from name)
                    foreach (var kvp in prefabs)
                    {
                        if (kvp.Value != null && name.Contains(kvp.Value.name))
                        {
                            prefabGo = kvp.Value;
                            break;
                        }
                    }
                }

                var enemy = prefabGo?.GetComponent<Battle.Enemies.Enemy>();
                if (enemy == null) continue;

                var uiGo = UnityEngine.Object.Instantiate(elementPrefab.gameObject, container);
                var element = uiGo.GetComponent<Battle.EnemyInfoElement>();
                element.SetEnemy(enemy);
                elements.Enqueue(element);
                created++;
            }

            // Show the scroll
            if (created > 0)
            {
                try
                {
                    var scrollAnim = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_scrollAnim")?.GetValue(eim) as UnityEngine.Animator;
                    scrollAnim?.SetTrigger(UnityEngine.Animator.StringToHash("RollDown"));
                    var mask = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_mask")?.GetValue(eim) as UnityEngine.UI.Mask;
                    if (mask != null)
                    {
                        var fullHeight = (float)(HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_maskFullHeight")?.GetValue(eim) ?? 6.195f);
                        var sd = mask.rectTransform.sizeDelta;
                        sd.y = fullHeight;
                        mask.rectTransform.sizeDelta = sd;
                    }
                }
                catch { }
            }

            // Update the "+N" indicator
            var moreField = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_moreUpcomingEnemiesIndicator");
            var moreText = moreField?.GetValue(eim) as TMPro.TMP_Text;
            if (moreText != null)
                moreText.gameObject.SetActive(false);

            _lastSyncedUpcoming = hostNames != null ? new List<string>(hostNames) : null;
            _log.LogInfo($"[EnemyApplier] Upcoming sync: rebuilt {oldCount} → {created} (host={hostNames.Count}, names={string.Join(",", hostNames)})");
        }
        catch (System.Exception ex)
        {
            _log.LogWarning($"[EnemyApplier] SyncUpcomingEnemies failed: {ex.Message}");
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

            // Initialize the enemy — this sets up HP bar, health text, animations.
            // Without this, HealthBarBarSprite is null and HP display doesn't work.
            try
            {
                var statusData = UnityEngine.Object.FindObjectOfType<Battle.StatusEffects.StatusEffectData>();
                var relicMgrs = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                var relicMgr = relicMgrs.Length > 0 ? relicMgrs[0] : null;
                enemy.Initialize(statusData, em, relicMgr, prefab.name);
                _log.LogInfo($"[EnemyApplier] Initialized enemy '{prefab.name}' (HP bar setup)");
            }
            catch (Exception initEx)
            {
                _log.LogWarning($"[EnemyApplier] Enemy.Initialize failed: {initEx.Message}");
            }

            // Try AddEnemy but don't crash if EnemyManager isn't initialized (slots null)
            try
            {
                em.AddEnemy(enemy, entry.SlotIndex, entry.IsFlying);
            }
            catch (Exception addEx)
            {
                _log.LogWarning($"[EnemyApplier] AddEnemy failed (EnemyManager may not be initialized): {addEx.Message}");
                // Add to Enemies list directly via reflection
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

            // Set HP AFTER AddEnemy/Initialize — Initialize resets HP to max
            SetMaxHealth(enemy, entry.MaxHealth);
            enemy.CurrentHealth = entry.CurrentHealth;
            ForceUpdateHealthBar(enemy);

            _log.LogInfo($"[EnemyApplier] Spawned '{entry.EnemyName}' at ({entry.PosX:F1},{entry.PosY:F1}) slot={entry.SlotIndex} guid={entry.Id} hp={enemy.CurrentHealth}/{entry.MaxHealth}");
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

    /// <summary>
    /// Call the enemy's protected UpdateHealthBar method via reflection to refresh the HP bar UI.
    /// Without this, setting CurrentHealth directly doesn't update the visual bar or text.
    /// </summary>
    private static void ForceUpdateHealthBar(Enemy enemy)
    {
        try
        {
            var method = AccessTools.Method(typeof(Enemy), "UpdateHealthBar");
            method?.Invoke(enemy, null);
        }
        catch { }
    }

    /// <summary>
    /// Sync status effects from host to client enemy. Clears existing effects
    /// and re-applies from the snapshot so the client's visual icons match.
    /// </summary>
    private void SyncStatusEffects(Enemy enemy, Snapshots.EnemyEntry entry)
    {
        if (entry.StatusEffects == null || entry.StatusEffects.Count == 0)
            return;

        try
        {
            // Get the internal _statusEffects list
            var statusField = AccessTools.Field(typeof(Enemy), "_statusEffects");
            var effects = statusField?.GetValue(enemy) as System.Collections.Generic.List<Battle.StatusEffects.StatusEffect>;
            if (effects == null) return;

            // Build a set of what the host has
            var hostEffects = new Dictionary<Battle.StatusEffects.StatusEffectType, int>();
            foreach (var se in entry.StatusEffects)
            {
                var type = (Battle.StatusEffects.StatusEffectType)se.EffectType;
                hostEffects[type] = se.Intensity;
            }

            // Update existing effects and add missing ones
            var existingTypes = new HashSet<Battle.StatusEffects.StatusEffectType>();
            foreach (var eff in effects)
                existingTypes.Add(eff.EffectType);

            foreach (var kvp in hostEffects)
            {
                if (existingTypes.Contains(kvp.Key))
                {
                    // Update intensity
                    foreach (var eff in effects)
                    {
                        if (eff.EffectType == kvp.Key)
                        {
                            eff.Intensity = kvp.Value;
                            break;
                        }
                    }
                }
                else
                {
                    // Add new effect
                    try
                    {
                        enemy.ApplyStatusEffect(
                            new Battle.StatusEffects.StatusEffect(kvp.Key, kvp.Value),
                            Battle.StatusEffects.StatusEffectSource.PLAYER,
                            allowKnockOnEffects: false);
                    }
                    catch { }
                }
            }

            // Remove effects the host doesn't have
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                if (!hostEffects.ContainsKey(effects[i].EffectType))
                {
                    try { enemy.RemoveEffect(effects[i].EffectType); }
                    catch { effects.RemoveAt(i); }
                }
            }

            // Refresh the status effect UI icons
            try
            {
                var uiField = AccessTools.Field(typeof(Enemy), "_statusEffectUI");
                var ui = uiField?.GetValue(enemy);
                if (ui != null)
                {
                    var updateMethod = AccessTools.Method(ui.GetType(), "UpdateStatusEffects");
                    updateMethod?.Invoke(ui, new object[] { enemy.StatusEffects });
                }
            }
            catch { }
        }
        catch (System.Exception ex)
        {
            _log.LogWarning($"[EnemyApplier] SyncStatusEffects failed for '{enemy.locKey}': {ex.Message}");
        }
    }
}
