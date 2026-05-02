using System;
using System.Collections.Generic;
using System.Linq;
using Battle.Enemies;
using BepInEx.Logging;
using Data;
using DG.Tweening;
using HarmonyLib;
using Loading;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.GameState.Appliers;

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
    // Cache of host enemy names that have no matching prefab on the client.
    // Without this, Resources.FindObjectsOfTypeAll<Enemy>() runs every heartbeat
    // for runtime-only variants like "Knight_Variant_4" — extremely expensive.
    private readonly HashSet<string> _missingPrefabs = new HashSet<string>();

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
                MapStateApplier.ClientWaitingMessage = "Waiting for other players...";
            }

            // Sync pachinkoBallSpawnLocation so boss fights (SlimeBoss) which alternate
            // this position each turn keep the client's aim origin aligned with the host.
            if (snapshot.BallSpawnX != 0f || snapshot.BallSpawnY != 0f)
            {
                var spawnBc = UnityEngine.Object.FindObjectOfType<Battle.BattleController>();
                if (spawnBc != null)
                {
                    spawnBc.pachinkoBallSpawnLocation = new UnityEngine.Vector2(snapshot.BallSpawnX, snapshot.BallSpawnY);
                }
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

            // Pass 1: match host enemies to client enemies by GUID first, then by name+position
            foreach (var entry in snapshot.Enemies)
            {
                // Try GUID match first
                var match = FindByGuid(entry.Id);
                var matchedByGuid = match != null && !matched.Contains(match);
                if (!matchedByGuid)
                {
                    // Fallback to locKey + position match (used on first-bind only)
                    match = FindBestMatch(liveEnemies, entry, matched);
                    if (match != null)
                    {
                        _log.LogInfo($"[EnemyApplier] First-bind match: '{match.locKey}' at ({match.transform.position.x:F1},{match.transform.position.y:F1}) → guid={entry.Id}");
                    }
                }

                if (match != null)
                {
                    // Check if the enemy type changed (e.g. Stump → StumpDead)
                    // If the name doesn't match, destroy the old and spawn the new —
                    // BUT only if the new prefab is actually known. Otherwise keep the
                    // existing enemy alive (host runtime variants like Knight_Variant_4
                    // aren't in the client prefab cache; destroying then failing to
                    // respawn caused a per-heartbeat flicker).
                    var matchName = match.gameObject.name.Replace("(Clone)", string.Empty).Trim();
                    var hostName = (entry.EnemyName ?? string.Empty).Replace("(Clone)", string.Empty).Trim();
                    var typeMismatch = !string.IsNullOrEmpty(hostName) && matchName != hostName;
                    var newPrefabKnown = typeMismatch && !_missingPrefabs.Contains(hostName) && FindEnemyPrefab(hostName) != null;
                    if (typeMismatch && newPrefabKnown)
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
                            SyncShield(spawned, entry);
                            ApplyC19Extra(spawned, entry);
                        }
                    }
                    else
                    {
                        // Same type — update state
                        SetMaxHealth(match, entry.MaxHealth);
                        match.CurrentHealth = entry.CurrentHealth;
                        // Smoothly tween to the host position when the delta is a plausible
                        // walk step; snap for first-bind and large jumps (teleports/respawn).
                        ApplyPosition(match, entry, firstBind: false);
                        ForceUpdateHealthBar(match);
                        SyncStatusEffects(match, entry);
                        SyncShield(match, entry);
                        ApplyC19Extra(match, entry);
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
                        ApplyC19Extra(spawned, entry);
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

            if (created > 0 || destroyed > 0)
            {
                _log.LogInfo($"[EnemyApplier] RESULT: Updated={updated}, Created={created}, Destroyed={destroyed} " +
                    $"(host={snapshot.Enemies.Count}, client_before={liveEnemies.Count}, battle={snapshot.BattleStateName})");
            }

            // === Post-apply verification ===
            VerifyEnemyState(snapshot);

            // Sync upcoming enemy preview from host's actual list
            SyncUpcomingEnemies(snapshot.UpcomingEnemyNames);
        }
        catch (Exception ex)
        {
            _log.LogError($"[EnemyApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Post-apply verification: spot-check first 3 enemies to confirm health was applied.
    /// Logs MISMATCH warnings for any differences, INFO on success.
    /// </summary>
    private void VerifyEnemyState(EnemyStateSnapshot snapshot)
    {
        try
        {
            var checked_ = 0;
            const int MAX_CHECK = 3;

            foreach (var entry in snapshot.Enemies)
            {
                if (checked_ >= MAX_CHECK)
                {
                    break;
                }

                var enemy = FindByGuid(entry.Id);
                if (enemy == null)
                {
                    continue;
                }

                checked_++;
                var actualHp = enemy.CurrentHealth;
                if (Math.Abs(actualHp - entry.CurrentHealth) > 0.1f)
                {
                    _log.LogWarning($"[Verify] MISMATCH enemy '{entry.LocKey}' (guid={entry.Id}) health: actual={actualHp:F1} expected={entry.CurrentHealth:F1}");
                }

                var maxField = AccessTools.Field(typeof(Enemy), "_maxHealth");
                var actualMax = maxField != null ? (float)maxField.GetValue(enemy) : -1f;
                if (entry.MaxHealth > 0 && Math.Abs(actualMax - entry.MaxHealth) > 0.1f)
                {
                    _log.LogWarning($"[Verify] MISMATCH enemy '{entry.LocKey}' (guid={entry.Id}) maxHealth: actual={actualMax:F1} expected={entry.MaxHealth:F1}");
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[Verify] EnemyState verification failed: {ex.Message}");
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
            if (eim == null)
            {
                return;
            }

            var elementsField = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_enemyInfoElements");
            var elements = elementsField?.GetValue(eim) as System.Collections.Generic.Queue<Battle.EnemyInfoElement>;
            if (elements == null)
            {
                return;
            }

            var hostCount = hostNames?.Count ?? 0;

            // Skip rebuild only if names are identical (avoid flicker on unchanged lists)
            if (_lastSyncedUpcoming != null && hostNames != null &&
                _lastSyncedUpcoming.Count == hostNames.Count &&
                _lastSyncedUpcoming.SequenceEqual(hostNames) &&
                elements.Count == hostCount)
            {
                return;
            }

            // Destroy all existing UI elements
            var oldCount = elements.Count;
            while (elements.Count > 0)
            {
                var el = elements.Dequeue();
                if (el != null)
                {
                    UnityEngine.Object.Destroy(el.gameObject);
                }
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
                catch
                {
                }

                if (oldCount > 0)
                {
                    _log.LogInfo($"[EnemyApplier] Upcoming sync: cleared {oldCount} → 0");
                }

                return;
            }

            // Rebuild UI from host enemy names using prefab cache
            var prefabs = Loading.AssetLoading.Instance?.EnemyPrefabs;
            var containerField = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_enemyInfoContainer");
            var container = containerField?.GetValue(eim) as UnityEngine.Transform;
            var prefabField = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_enemyInfoElementPrefab");
            var elementPrefab = prefabField?.GetValue(eim) as Battle.EnemyInfoElement;

            if (prefabs == null || container == null || elementPrefab == null)
            {
                return;
            }

            var created = 0;
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
                if (enemy == null)
                {
                    continue;
                }

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
                catch
                {
                }
            }

            // Update the "+N" indicator
            var moreField = HarmonyLib.AccessTools.Field(typeof(Battle.EnemyInfoManager), "_moreUpcomingEnemiesIndicator");
            var moreText = moreField?.GetValue(eim) as TMPro.TMP_Text;
            moreText?.gameObject.SetActive(false);

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
        if (cache != null && cache.Count > 0)
        {
            return;
        }

        var battle = StaticGameData.dataToLoad as MapDataBattle;
        if (battle == null)
        {
            _log.LogWarning("[EnemyApplier] Cannot load enemy prefabs — no MapDataBattle in dataToLoad");
            return;
        }

        var loaded = 0;
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
                    if (spawn?.spawnData?.enemyAssetReference == null)
                    {
                        continue;
                    }

                    var key = spawn.spawnData.enemyAssetReference.RuntimeKey.ToString();
                    if (cache.ContainsKey(key))
                    {
                        continue;
                    }

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
                if (wg?.waveData == null)
                {
                    continue;
                }

                foreach (var wd in wg.waveData)
                {
                    try
                    {
                        if (wd?.spawnData?.enemyAssetReference == null)
                        {
                            continue;
                        }

                        var key = wd.spawnData.enemyAssetReference.RuntimeKey.ToString();
                        if (cache.ContainsKey(key))
                        {
                            continue;
                        }

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
        if (string.IsNullOrEmpty(guid))
        {
            return null;
        }

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
        var bestDist = float.MaxValue;

        foreach (var e in liveEnemies)
        {
            if (e == null || alreadyMatched.Contains(e))
            {
                continue;
            }

            if (e.locKey != entry.LocKey)
            {
                continue;
            }

            var dx = e.transform.position.x - entry.PosX;
            var dy = e.transform.position.y - entry.PosY;
            var dist = dx * dx + dy * dy;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = e;
            }
        }

        if (best != null)
        {
            return best;
        }

        // Cross-locKey position fallback ONLY for near-exact matches (<= 0.5 units).
        // Loose 3-unit fallback was wrong-matching different enemies that happened to
        // share a row (e.g. Knight_Variant_4 at x=1.0 → ArcherKnight at x=3.8) and
        // then triggering a destroy/respawn flicker on every heartbeat.
        foreach (var e in liveEnemies)
        {
            if (e == null || alreadyMatched.Contains(e))
            {
                continue;
            }

            var dx = e.transform.position.x - entry.PosX;
            var dy = e.transform.position.y - entry.PosY;
            var dist = dx * dx + dy * dy;
            if (dist < 0.25f && dist < bestDist) // 0.5 units
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
            if (prefab == null)
            {
                return null;
            }

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
                var statusDatas = UnityEngine.Resources.FindObjectsOfTypeAll<Battle.StatusEffects.StatusEffectData>();
                var statusData = statusDatas.Length > 0 ? statusDatas[0] : null;
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
                    {
                        enemiesList.Add(enemy);
                    }
                }
                catch
                {
                }
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
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var cleanName = name.Replace("(Clone)", string.Empty).Trim();

        // Short-circuit known-missing names — Resources.FindObjectsOfTypeAll is very
        // expensive and runtime-only variants (Knight_Variant_4, etc.) will never
        // appear in any cache.
        if (_missingPrefabs.Contains(cleanName))
        {
            return null;
        }

        // Strategy 1: AssetLoading cache (keyed by RuntimeKey, so match by prefab name)
        var cache = AssetLoading.Instance?.EnemyPrefabs;
        if (cache != null && cache.Count > 0)
        {
            foreach (var kvp in cache)
            {
                if (kvp.Value != null && kvp.Value.name == cleanName)
                {
                    return kvp.Value;
                }
            }
            // Try partial match (enemy prefabs sometimes have variant names)
            foreach (var kvp in cache)
            {
                if (kvp.Value != null && (kvp.Value.name.Contains(cleanName) || cleanName.Contains(kvp.Value.name)))
                {
                    return kvp.Value;
                }
            }
        }

        // Strategy 2: search all loaded GameObjects with Enemy component (finds prefabs)
        var allEnemies = Resources.FindObjectsOfTypeAll<Battle.Enemies.Enemy>();
        foreach (var e in allEnemies)
        {
            if (e != null && e.gameObject.name == cleanName && e.gameObject.scene.name == null)
            {
                return e.gameObject;
            }
        }

        // Strategy 3: partial name match on prefabs
        foreach (var e in allEnemies)
        {
            if (e != null && e.gameObject.scene.name == null &&
                (e.gameObject.name.Contains(cleanName) || cleanName.Contains(e.gameObject.name)))
            {
                return e.gameObject;
            }
        }

        // Cache the miss so we don't run Resources.FindObjectsOfTypeAll every heartbeat.
        _missingPrefabs.Add(cleanName);
        _log.LogWarning($"[EnemyApplier] No prefab found for '{cleanName}' (cache={cache?.Count ?? -1}, resources={allEnemies.Length}) — caching as missing");
        return null;
    }

    /// <summary>
    /// Smoothly move a client enemy to the host-reported position when the delta matches
    /// a normal walk step; snap for large jumps (teleport, respawn, first bind).
    /// </summary>
    private static void ApplyPosition(Enemy enemy, Snapshots.EnemyEntry entry, bool firstBind)
    {
        var current = enemy.transform.position;
        var target = new Vector3(entry.PosX, entry.PosY, current.z);
        var dx = Mathf.Abs(target.x - current.x);
        var dy = Mathf.Abs(target.y - current.y);

        // Walk distance window: any horizontal delta up to ~8 units (typical max slot spacing).
        // Y delta must be tiny — vertical jumps are boss animations or respawn and should snap.
        var canTween = !firstBind && dx > 0.05f && dx < 8f && dy < 0.5f;

        if (!canTween)
        {
            enemy.transform.position = target;
            return;
        }

        var id = "coopEnemyMove_" + (entry.Id ?? enemy.GetInstanceID().ToString());
        DG.Tweening.DOTween.Kill(id);

        StartWalkAnim(enemy);
        var duration = Mathf.Clamp(dx / 4f, 0.4f, 1.2f);
        var enemyRef = enemy;
        var tween = enemy.transform.DOMoveX(target.x, duration).SetEase(DG.Tweening.Ease.InOutSine);
        tween.SetId(id);
        tween.onComplete = () => StopWalkAnim(enemyRef);

        if (dy > 0.01f)
        {
            var p = enemy.transform.position;
            p.y = target.y;
            enemy.transform.position = p;
        }
    }

    private static void StartWalkAnim(Enemy enemy)
    {
        try
        {
            if (!(enemy is Battle.Enemies.WalkEnemy))
            {
                return;
            }

            var animField = AccessTools.Field(typeof(Enemy), "_anim");
            var anim = animField?.GetValue(enemy) as Animator;
            if (anim == null || anim.runtimeAnimatorController == null)
            {
                return;
            }

            var moveAnimField = AccessTools.Field(typeof(Battle.Enemies.WalkEnemy), "_moveAnimName");
            var moveAnim = moveAnimField?.GetValue(enemy) as string ?? "Running";
            foreach (var p in anim.parameters)
            {
                if (p.name == moveAnim && p.type == AnimatorControllerParameterType.Bool)
                {
                    anim.SetBool(moveAnim, true);
                    return;
                }
            }
        }
        catch
        {
        }
    }

    private static void StopWalkAnim(Enemy enemy)
    {
        try
        {
            if (enemy == null)
            {
                return;
            }

            if (!(enemy is Battle.Enemies.WalkEnemy))
            {
                return;
            }

            var animField = AccessTools.Field(typeof(Enemy), "_anim");
            var anim = animField?.GetValue(enemy) as Animator;
            if (anim == null || anim.runtimeAnimatorController == null)
            {
                return;
            }

            var moveAnimField = AccessTools.Field(typeof(Battle.Enemies.WalkEnemy), "_moveAnimName");
            var moveAnim = moveAnimField?.GetValue(enemy) as string ?? "Running";
            foreach (var p in anim.parameters)
            {
                if (p.name == moveAnim && p.type == AnimatorControllerParameterType.Bool)
                {
                    anim.SetBool(moveAnim, false);
                    return;
                }
            }
        }
        catch
        {
        }
    }

    private static void SetMaxHealth(Enemy enemy, float maxHealth)
    {
        if (maxHealth <= 0)
        {
            return;
        }

        var field = AccessTools.Field(typeof(Enemy), "_maxHealth");
        field?.SetValue(enemy, maxHealth);
    }

    /// <summary>
    /// Sync a ShieldEnemy's BarricadeEnemy child (barricade HP, active/dead state).
    /// BarricadeEnemy is not in EnemyManager.Enemies so it must be driven through
    /// the parent ShieldEnemy's entry.
    /// </summary>
    private void SyncShield(Enemy enemy, Snapshots.EnemyEntry entry)
    {
        try
        {
            if (!entry.HasShield)
            {
                return;
            }

            if (!(enemy is ShieldEnemy se))
            {
                return;
            }

            var shield = se.shield;
            if (shield == null)
            {
                return;
            }

            var shieldGO = shield.gameObject;
            var clientActive = shieldGO.activeInHierarchy;

            // Host shield dead — hide it on client.
            if (!entry.ShieldActive)
            {
                if (clientActive)
                {
                    // Taking HP to 0 before disabling keeps the death callback path
                    // consistent with a natural kill (OnDisable invokes _cbOnDamagedAnimationEnd).
                    shield.CurrentHealth = 0f;
                    ForceUpdateHealthBar(shield);
                    shieldGO.SetActive(false);
                    _log.LogInfo($"[EnemyApplier] Shield KILLED on '{enemy.locKey}' (guid={entry.Id})");
                }

                return;
            }

            // Host shield alive — ensure it's active and HP matches.
            if (!clientActive)
            {
                shieldGO.SetActive(true);
                _log.LogInfo($"[EnemyApplier] Shield RESURRECTED on '{enemy.locKey}' (guid={entry.Id})");
            }

            SetMaxHealth(shield, entry.ShieldMaxHealth);
            if (System.Math.Abs(shield.CurrentHealth - entry.ShieldCurrentHealth) > 0.01f)
            {
                shield.CurrentHealth = entry.ShieldCurrentHealth;
                ForceUpdateHealthBar(shield);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[EnemyApplier] SyncShield failed for '{enemy?.locKey}': {ex.Message}");
        }
    }

    /// <summary>
    /// Apply the cruciball "extra enemy" HP bar background (blue border) when the host
    /// flagged this enemy as a c19 bonus spawn. Idempotent — only switches the sprite
    /// when it isn't already the c19 background.
    /// </summary>
    private void ApplyC19Extra(Enemy enemy, Snapshots.EnemyEntry entry)
    {
        try
        {
            if (!entry.IsC19Extra)
            {
                return;
            }

            var slider = enemy?.HealthBarBarSprite;
            if (slider == null)
            {
                return;
            }

            var bgField = AccessTools.Field(typeof(UpdateSlider), "_background");
            var c19Field = AccessTools.Field(typeof(UpdateSlider), "_c19Background");
            var bgImg = bgField?.GetValue(slider) as UnityEngine.UI.Image;
            var c19Sprite = c19Field?.GetValue(slider) as UnityEngine.Sprite;
            if (bgImg == null || c19Sprite == null)
            {
                return;
            }

            if (bgImg.sprite == c19Sprite)
            {
                return;
            }

            slider.SetIsC19ExtraEnemy();
            _log.LogInfo($"[EnemyApplier] Applied c19 HP bar to '{enemy.locKey}' (guid={entry.Id})");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[EnemyApplier] ApplyC19Extra failed for '{enemy?.locKey}': {ex.Message}");
        }
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
        catch
        {
        }
    }

    /// <summary>
    /// Sync status effects from host to client enemy.
    /// Directly manipulates the _statusEffects list and updates the UI.
    /// NEVER calls ApplyStatusEffect — that method runs relic checks, intensity
    /// modifiers, knock-on effects, and other game logic that corrupts the host values.
    /// </summary>
    private void SyncStatusEffects(Enemy enemy, Snapshots.EnemyEntry entry)
    {
        try
        {
            var statusField = AccessTools.Field(typeof(Enemy), "_statusEffects");
            var effects = statusField?.GetValue(enemy) as System.Collections.Generic.List<Battle.StatusEffects.StatusEffect>;
            if (effects == null)
            {
                return;
            }

            var uiField = AccessTools.Field(typeof(Enemy), "_statusEffectUI");
            var ui = uiField?.GetValue(enemy) as Battle.StatusEffects.StatusEffectIconManager;

            // Build what the host has
            var hostEffects = new Dictionary<Battle.StatusEffects.StatusEffectType, int>();
            if (entry.StatusEffects != null)
            {
                foreach (var se in entry.StatusEffects)
                {
                    var type = (Battle.StatusEffects.StatusEffectType)se.EffectType;
                    hostEffects[type] = se.Intensity;
                }
            }

            // Remove effects that the host doesn't have
            for (var i = effects.Count - 1; i >= 0; i--)
            {
                if (!hostEffects.ContainsKey(effects[i].EffectType))
                {
                    _log.LogInfo($"[EnemyApplier] StatusSync REMOVING '{enemy.locKey}': {effects[i].EffectType}({(int)effects[i].EffectType})={effects[i].Intensity}");
                    var removed = effects[i];
                    removed.Intensity = 0;
                    try
                    {
                        ui?.UpdateStatusEffect(removed);
                    }
                    catch
                    {
                    }

                    effects.RemoveAt(i);
                }
            }

            // Update existing / add missing — direct list manipulation only
            foreach (var kvp in hostEffects)
            {
                var found = false;
                foreach (var eff in effects)
                {
                    if (eff.EffectType == kvp.Key)
                    {
                        eff.Intensity = kvp.Value;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    _log.LogInfo($"[EnemyApplier] StatusSync ADDING '{enemy.locKey}': {kvp.Key}({(int)kvp.Key})={kvp.Value}");
                    effects.Add(new Battle.StatusEffects.StatusEffect(kvp.Key, kvp.Value));
                }
            }

            // Ensure EffectData is set — Initialize may have failed to find it via FindObjectOfType
            if (ui != null && ui.EffectData == null)
            {
                var statusDatas = UnityEngine.Resources.FindObjectsOfTypeAll<Battle.StatusEffects.StatusEffectData>();
                if (statusDatas.Length > 0)
                {
                    ui.EffectData = statusDatas[0];
                    _log.LogInfo($"[EnemyApplier] StatusSync set EffectData for '{enemy.locKey}' from Resources");
                }
            }

            var hasUiData = ui != null && ui.EffectData != null;
            if (hasUiData)
            {
                try
                {
                    ui.UpdateStatusEffects(enemy.StatusEffects);
                }
                catch
                {
                }
            }
            else if (ui != null)
            {
                _log.LogWarning($"[EnemyApplier] StatusSync UI has null EffectData for '{enemy.locKey}' — icons won't render");
            }
        }
        catch (System.Exception ex)
        {
            _log.LogWarning($"[EnemyApplier] SyncStatusEffects failed for '{enemy.locKey}': {ex.Message}");
        }
    }
}
