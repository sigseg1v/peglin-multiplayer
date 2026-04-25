using System;
using System.Collections.Generic;
using Battle;
using BepInEx.Logging;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.GameState.Appliers;

/// <summary>
/// Syncs pegboard state from host to client using GUID-based tracking.
///
/// PegManager stores three separate lists: _allPegs, _bombs, _bouncerPegs.
/// Bombs and bouncers are NOT in allPegs — they must be handled separately.
///
/// Three-phase matching:
/// 1. GUID match with type validation (subsequent syncs, best case)
/// 2. Type-aware position match: bombs→bombs, bouncers→bouncers, regulars→regulars
/// 3. Reposition: grab any unmatched client peg and move it to host position
///
/// After matching, deactivate extras on client that don't exist on host.
/// </summary>
public class PegboardStateApplier : IGameStateApplier<PegboardStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly PegIdentifier _pegId;

    /// <summary>Tracks vines created on the client, keyed by sorted peg GUID pair.</summary>
    private readonly Dictionary<string, GameObject> _clientVines = new Dictionary<string, GameObject>();

    public PegboardStateApplier(ManualLogSource log, PegIdentifier pegId)
    {
        _log = log;
        _pegId = pegId;
    }

    private static string VineKey(string guid1, string guid2)
    {
        return string.Compare(guid1, guid2, System.StringComparison.Ordinal) < 0
            ? $"{guid1}|{guid2}" : $"{guid2}|{guid1}";
    }

    public void Apply(PegboardStateSnapshot snapshot)
    {
        try
        {
            // Reset per-heartbeat tracking for movement parent sync
            _syncedMovementParents.Clear();

            if (snapshot.Pegs == null || snapshot.Pegs.Count == 0)
            {
                _log.LogInfo("[PegboardApplier] No pegs in snapshot.");
                return;
            }

            var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
            var pm = bc?.pegManager;
            if (pm == null || pm.allPegs == null)
            {
                _log.LogInfo($"[PegboardApplier] No PegManager in scene. Snapshot has {snapshot.TotalPegCount} pegs.");
                return;
            }

            var clientPegs = pm.allPegs;
            var bombsField = HarmonyLib.AccessTools.Field(typeof(PegManager), "_bombs");
            var clientBombs = bombsField?.GetValue(pm) as List<Bomb>;
            var clientBouncers = pm.bouncerPegs;

            int idxMatched = 0, guidMatched = 0, posMatched = 0, repositioned = 0, typeChanged = 0,
                destroyed = 0, reactivated = 0, cleared = 0, missed = 0, guidTypeInvalid = 0,
                structMatched = 0;
            var matchedPegs = new HashSet<Peg>();

            var unmatchedEntries = new List<PegEntry>();

            int pegsCount = clientPegs?.Count ?? 0;
            int bombsCount = clientBombs?.Count ?? 0;
            int bouncersCount = clientBouncers?.Count ?? 0;

            // Pre-build a structural index of unbound client pegs keyed by
            // (parent_name, localPos). This key is stable across host/client
            // because it's baked into the prefab hierarchy — unlike
            // pm.allPegs ordering which is non-deterministic (race condition
            // during PegLayoutLoader, especially for MinotaurLayout).
            var structIndex = BuildStructIndex(clientPegs, clientBombs, clientBouncers);

            // ===== PHASE 0, 1 & 2: Struct, GUID, then type-aware position =====
            foreach (var entry in snapshot.Pegs)
            {
                Peg peg = null;

                // Phase 0: STRUCTURAL-KEY MATCHING (first-sync robustness).
                // Match by (parent_name, localPos) — stable across host/client
                // because it's baked into the prefab hierarchy. This avoids the
                // non-deterministic pm.allPegs ordering problem (especially
                // MinotaurLayout) where list-index binding corrupts every peg.
                //
                // Only runs when the target peg has no GUID yet (first-time bind)
                // AND the type matches — never re-bind an already-GUID'd peg.
                Peg structCandidate = ResolveByStructKey(structIndex, entry);
                if (structCandidate != null
                    && !matchedPegs.Contains(structCandidate)
                    && string.IsNullOrEmpty(_pegId.GetGuid(structCandidate))
                    && TypeMatches(structCandidate, entry))
                {
                    peg = structCandidate;
                    structMatched++;
                    if (entry.PegType != 0 || entry.IsBomb || entry.IsBouncer || entry.HasLpm)
                    {
                        var keyKind = entry.HasLpm ? $"sibling#{entry.SiblingIndex}" : $"lp({entry.LocalPosX:F2},{entry.LocalPosY:F2})";
                        _log.LogInfo($"[PegboardApplier] STRUCT BIND: key=({entry.ParentName}|{keyKind}) " +
                            $"guid={entry.Guid} type={entry.PegTypeName} lpm={entry.HasLpm} " +
                            $"hostPos=({entry.PosX:F1},{entry.PosY:F1}) " +
                            $"clientPos=({structCandidate.transform.position.x:F1},{structCandidate.transform.position.y:F1})");
                    }
                }

                // Phase 0.5: INDEX-BASED fallback — only if the struct key is
                // missing (e.g. old host without the new field). Includes a
                // position-sanity guard to reject obviously-wrong bindings.
                if (peg == null && string.IsNullOrEmpty(entry.ParentName))
                {
                    Peg indexCandidate = ResolveByIndex(entry.Index, pegsCount, bombsCount,
                        clientPegs, clientBombs, clientBouncers);
                    if (indexCandidate != null
                        && !matchedPegs.Contains(indexCandidate)
                        && string.IsNullOrEmpty(_pegId.GetGuid(indexCandidate))
                        && TypeMatches(indexCandidate, entry)
                        && IndexBindPositionSane(indexCandidate, entry))
                    {
                        peg = indexCandidate;
                        idxMatched++;
                    }
                }

                // Phase 1: GUID match with type validation
                if (peg == null && !string.IsNullOrEmpty(entry.Guid))
                {
                    peg = _pegId.Find(entry.Guid);
                    if (peg != null)
                    {
                        // Validate: bomb GUIDs → bombs, bouncer GUIDs → bouncers, regular → neither
                        bool pegIsBomb = peg is Bomb;
                        bool pegIsBouncer = peg is BouncerPeg;
                        bool typeOk = (entry.IsBomb == pegIsBomb) && (entry.IsBouncer == pegIsBouncer);
                        if (!typeOk)
                        {
                            _log.LogWarning($"[PegboardApplier] GUID type mismatch: {entry.Guid} " +
                                $"entry(bomb={entry.IsBomb},bouncer={entry.IsBouncer}) " +
                                $"peg({peg.GetType().Name}) — re-matching by position");
                            guidTypeInvalid++;
                            peg = null;
                        }
                        else
                        {
                            guidMatched++;
                        }
                    }
                }

                // Phase 2: type-aware position match
                // STRICT same-type only: bombs→bombs, bouncers→bouncers, regulars→regulars.
                // NO cross-type fallback — mixing types here was corrupting the _bombs list
                // with unrelated entries and triggering GUID type mismatches every heartbeat.
                if (peg == null)
                {
                    if (entry.IsBomb)
                        peg = FindClosestUnmatched(entry, null, clientBombs, null, matchedPegs, 3f);
                    else if (entry.IsBouncer)
                        peg = FindClosestUnmatched(entry, null, null, clientBouncers, matchedPegs, 3f);
                    else
                        peg = FindClosestUnmatched(entry, clientPegs, null, null, matchedPegs, 1f);

                    if (peg != null)
                    {
                        posMatched++;
                        _log.LogWarning($"[PegboardApplier] POS BIND (idx fallback): idx={entry.Index} guid={entry.Guid} " +
                            $"type={entry.PegTypeName} hostPos=({entry.PosX:F1},{entry.PosY:F1}) " +
                            $"clientPos=({peg.transform.position.x:F1},{peg.transform.position.y:F1})");
                    }
                }

                if (peg != null)
                {
                    matchedPegs.Add(peg);

                    // If client peg is a Bomb but host says it should NOT be a bomb,
                    // deactivate the bomb — Bomb.ConvertPegToType(REGULAR) doesn't
                    // change the visual. The bomb stays visible as a bomb forever.
                    var targetType = (Peg.PegType)entry.PegType;
                    if (peg is Bomb && !entry.IsBomb && targetType != Peg.PegType.BOMB)
                    {
                        _log.LogWarning($"[PegboardApplier] BOMB DEACTIVATED: {entry.Guid} client is Bomb but host says {entry.PegTypeName}");
                        peg.gameObject.SetActive(false);
                    }
                    else
                    {
                        ApplyPegState(peg, entry, clientBombs, ref typeChanged, ref destroyed, ref reactivated, ref cleared);
                    }

                    // Bomb matching details only logged when host/client state diverges
                    // (was a per-heartbeat 6× spam for stable bomb fields).
                    if (entry.IsBomb && peg is Bomb bombPeg)
                    {
                        bool pegDisabled = false;
                        try { pegDisabled = peg.IsDisabled(); } catch { }
                        bool stateMatchesHost = pegDisabled == entry.IsDestroyed && bombPeg.HitCount == entry.HitCount;
                        if (!stateMatchesHost)
                        {
                            _log.LogInfo($"[PegboardApplier] BOMB DRIFT: guid={entry.Guid} " +
                                $"clientDisabled={pegDisabled} hostDestroyed={entry.IsDestroyed} " +
                                $"clientHits={bombPeg.HitCount} hostHits={entry.HitCount}");
                        }
                    }

                    SyncPegPosition(peg, entry);
                }
                else
                {
                    unmatchedEntries.Add(entry);
                }
            }

            // ===== PHASE 3: Reposition unmatched client pegs to host positions =====
            // Type-aware pools: bomb entries only take from clientBombs, bouncer
            // entries from clientBouncers, regular entries from clientPegs.
            // Cross-type grabs were scrambling bomb placement by reusing bombs
            // as regular pegs and vice versa.
            if (unmatchedEntries.Count > 0)
            {
                var availableRegulars = new List<Peg>();
                var availableBombs = new List<Peg>();
                var availableBouncers = new List<Peg>();

                foreach (var p in clientPegs)
                {
                    if (p != null && !matchedPegs.Contains(p))
                        availableRegulars.Add(p);
                }
                if (clientBombs != null)
                {
                    foreach (var b in clientBombs)
                    {
                        if (b != null && !matchedPegs.Contains(b))
                            availableBombs.Add(b);
                    }
                }
                if (clientBouncers != null)
                {
                    foreach (var bo in clientBouncers)
                    {
                        if (bo != null && !matchedPegs.Contains(bo))
                            availableBouncers.Add(bo);
                    }
                }

                Peg templatePeg = null;
                if (clientPegs.Count > 0)
                    templatePeg = clientPegs[0];

                foreach (var entry in unmatchedEntries)
                {
                    Peg peg = null;
                    List<Peg> pool = entry.IsBomb ? availableBombs
                        : entry.IsBouncer ? availableBouncers
                        : availableRegulars;

                    if (pool.Count > 0)
                    {
                        peg = pool[pool.Count - 1];
                        pool.RemoveAt(pool.Count - 1);
                        repositioned++;
                    }
                    else if (entry.IsBomb && availableRegulars.Count > 0)
                    {
                        int closestIdx = -1;
                        float closestDist = float.MaxValue;
                        for (int i = 0; i < availableRegulars.Count; i++)
                        {
                            var r = availableRegulars[i];
                            if (r == null) continue;
                            float dx = r.transform.position.x - entry.PosX;
                            float dy = r.transform.position.y - entry.PosY;
                            float d = dx * dx + dy * dy;
                            if (d < closestDist) { closestDist = d; closestIdx = i; }
                        }
                        if (closestIdx >= 0)
                        {
                            peg = availableRegulars[closestIdx];
                            availableRegulars.RemoveAt(closestIdx);
                            repositioned++;
                            _log.LogInfo($"[PegboardApplier] BOMB FROM REGULAR: guid={entry.Guid} " +
                                $"hostPos=({entry.PosX:F1},{entry.PosY:F1}) " +
                                $"converting regular peg at ({peg.transform.position.x:F1},{peg.transform.position.y:F1}) to bomb");
                        }
                        else
                        {
                            peg = SynthesizeBomb(entry, clientPegs, clientBombs);
                            if (peg == null)
                            {
                                missed++;
                                _log.LogWarning($"[PegboardApplier] MISSED unmatched entry guid={entry.Guid} " +
                                    $"hostPos=({entry.PosX:F1},{entry.PosY:F1}) bomb={entry.IsBomb} bouncer={entry.IsBouncer} " +
                                    $"— bomb synthesis failed");
                                continue;
                            }
                            repositioned++;
                        }
                    }
                    else if (entry.IsBomb)
                    {
                        // Host spawned a bomb (e.g. bob-orb) but every client regular
                        // peg is already GUID-matched — instantiate _bombPrefab directly.
                        peg = SynthesizeBomb(entry, clientPegs, clientBombs);
                        if (peg == null)
                        {
                            missed++;
                            _log.LogWarning($"[PegboardApplier] MISSED unmatched entry guid={entry.Guid} " +
                                $"hostPos=({entry.PosX:F1},{entry.PosY:F1}) bomb={entry.IsBomb} bouncer={entry.IsBouncer} " +
                                $"— bomb synthesis failed");
                            continue;
                        }
                        repositioned++;
                    }
                    else if (!entry.IsBomb && !entry.IsBouncer && templatePeg != null)
                    {
                        // Only clone regular pegs — bomb/bouncer prefabs aren't
                        // in clientPegs and shouldn't be fabricated from regulars.
                        var clone = UnityEngine.Object.Instantiate(templatePeg, templatePeg.transform.parent);
                        clone.gameObject.SetActive(true);
                        peg = clone;
                        try { pm.AddPeg(peg); } catch { clientPegs.Add(peg); }
                        repositioned++;
                    }
                    else
                    {
                        missed++;
                        _log.LogWarning($"[PegboardApplier] MISSED unmatched entry guid={entry.Guid} " +
                            $"hostPos=({entry.PosX:F1},{entry.PosY:F1}) bomb={entry.IsBomb} bouncer={entry.IsBouncer} " +
                            $"— no same-type client peg available");
                        continue;
                    }

                    peg.transform.position = new Vector3(entry.PosX, entry.PosY, peg.transform.position.z);
                    matchedPegs.Add(peg);
                    ApplyPegState(peg, entry, clientBombs, ref typeChanged, ref destroyed, ref reactivated, ref cleared);
                    SyncPegPosition(peg, entry);
                }
            }

            // ===== CLEANUP: Deactivate extra client pegs not in host snapshot =====
            // Build set of transforms that are parents of matched pegs — don't deactivate these
            var matchedParents = new HashSet<Transform>();
            foreach (var mp in matchedPegs)
            {
                if (mp != null)
                {
                    var p = mp.transform.parent;
                    while (p != null)
                    {
                        matchedParents.Add(p);
                        p = p.parent;
                    }
                }
            }

            int extrasRemoved = 0;
            foreach (var peg in clientPegs)
            {
                if (peg != null && peg.gameObject.activeSelf && !matchedPegs.Contains(peg)
                    && !matchedParents.Contains(peg.transform))
                {
                    peg.gameObject.SetActive(false);
                    extrasRemoved++;
                }
            }
            // Aggressive cleanup of stale unmatched bombs: the client's own RNG
            // bomb placement (via ConvertPegsToBombs) left bombs in _bombs that
            // the host never sees. Deactivating them isn't enough — they stay in
            // the list and re-trigger GUID type-mismatch warnings every heartbeat.
            // REMOVE them from the _bombs list, clear the GUID registry, destroy the GO.
            int staleBombsRemoved = 0;
            if (clientBombs != null)
            {
                for (int i = clientBombs.Count - 1; i >= 0; i--)
                {
                    var bomb = clientBombs[i];
                    if (bomb == null)
                    {
                        clientBombs.RemoveAt(i);
                        continue;
                    }
                    if (matchedPegs.Contains(bomb)) continue;
                    if (matchedParents.Contains(bomb.transform)) continue;

                    // Unmatched: host snapshot has no bomb corresponding to this
                    // client bomb. It's stale (from client-side RNG placement or
                    // a detonation the host rolled back). Purge it completely.
                    var staleGuid = _pegId.GetGuid(bomb);
                    if (!string.IsNullOrEmpty(staleGuid))
                    {
                        // Reflect into PegIdentifier internals — no public Remove.
                        try
                        {
                            var g2p = HarmonyLib.AccessTools.Field(typeof(PegIdentifier), "_guidToPeg")
                                ?.GetValue(_pegId) as System.Collections.IDictionary;
                            var p2g = HarmonyLib.AccessTools.Field(typeof(PegIdentifier), "_pegToGuid")
                                ?.GetValue(_pegId) as System.Collections.IDictionary;
                            g2p?.Remove(staleGuid);
                            p2g?.Remove(bomb);
                        }
                        catch { }
                    }

                    clientBombs.RemoveAt(i);
                    try { UnityEngine.Object.Destroy(bomb.gameObject); } catch { }
                    staleBombsRemoved++;
                    extrasRemoved++;
                }
            }
            if (staleBombsRemoved > 0)
                _log.LogWarning($"[PegboardApplier] Purged {staleBombsRemoved} stale client bombs (not in host snapshot)");
            if (clientBouncers != null)
            {
                foreach (var bouncer in clientBouncers)
                {
                    if (bouncer != null && bouncer.gameObject.activeSelf && !matchedPegs.Contains(bouncer)
                        && !matchedParents.Contains(bouncer.transform))
                    {
                        bouncer.gameObject.SetActive(false);
                        extrasRemoved++;
                    }
                }
            }

            int totalClient = clientPegs.Count + (clientBombs?.Count ?? 0) + (clientBouncers?.Count ?? 0);
            _log.LogInfo($"[PegboardApplier] StructMatched={structMatched}, IdxMatched={idxMatched}, GUIDMatched={guidMatched}, PosMatched={posMatched}, " +
                $"Repositioned={repositioned}, TypeChanged={typeChanged}, Destroyed={destroyed}, " +
                $"Reactivated={reactivated}, Cleared={cleared}, Missed={missed}, GUIDTypeInvalid={guidTypeInvalid}, " +
                $"ExtrasRemoved={extrasRemoved} " +
                $"(host={snapshot.TotalPegCount}, client={totalClient}, " +
                $"crit={snapshot.CritPegCount}, bomb={snapshot.BombPegCount}, " +
                $"reset={snapshot.ResetPegCount}, bouncer={snapshot.BouncerPegCount}, " +
                $"registry={_pegId.Count})");

            // Sync bramball vines
            SyncVines(snapshot, bc);

            // Per-bomb dump previously logged 6 lines per heartbeat — now only logged
            // when the client/host bomb count diverges (a real sync issue).
            int hostBombCount = snapshot.BombPegCount;
            int clientBombCount = clientBombs?.Count ?? 0;
            if (clientBombCount != hostBombCount)
            {
                LogActualPegState(clientPegs, clientBombs, clientBouncers);
                if (clientBombs != null)
                {
                    for (int i = 0; i < clientBombs.Count; i++)
                    {
                        var b = clientBombs[i];
                        if (b == null) { _log.LogInfo($"[PegboardApplier] CLIENT_BOMB[{i}] NULL"); continue; }
                        bool dis = false;
                        try { dis = b.IsDisabled(); } catch { }
                        var guid = _pegId.GetGuid(b) ?? "none";
                        _log.LogInfo($"[PegboardApplier] CLIENT_BOMB[{i}] guid={guid} " +
                            $"pos=({b.transform.position.x:F1},{b.transform.position.y:F1}) " +
                            $"type={b.pegType} active={b.gameObject.activeSelf} disabled={dis} hits={b.HitCount}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"[PegboardApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Sync a peg's position to the host's position.
    /// Looks up the final object via GUID (ConvertPegToType may create a new GO).
    /// For pegs under LinearPegMovement parents, adjusts the PARENT position
    /// so the entire row shifts correctly (avoids wrap-direction conflicts).
    /// Hard-snaps static pegs; soft-lerps other moving pegs.
    /// </summary>
    private HashSet<Transform> _syncedMovementParents = new HashSet<Transform>();

    public void ResetMovementParentTracking() => _syncedMovementParents.Clear();

    private void SyncPegPosition(Peg originalPeg, PegEntry entry)
    {
        var finalPeg = !string.IsNullOrEmpty(entry.Guid) ? _pegId.Find(entry.Guid) : null;
        if (finalPeg == null) finalPeg = originalPeg;

        var hostPos = new Vector3(entry.PosX, entry.PosY, finalPeg.transform.position.z);

        // Check for LinearPegMovement on the peg itself, its direct parent, or
        // grandparent (bombs are children of the original peg which is child of LPM row).
        var lpm = finalPeg.GetComponent<Battle.PegBehaviour.LinearPegMovement>();
        if (lpm == null && finalPeg.transform.parent != null)
            lpm = finalPeg.transform.parent.GetComponent<Battle.PegBehaviour.LinearPegMovement>();
        if (lpm == null && finalPeg.transform.parent?.parent != null)
            lpm = finalPeg.transform.parent.parent.GetComponent<Battle.PegBehaviour.LinearPegMovement>();

        // Also detect via the snapshot — host tells us this peg is under LPM
        if (lpm == null && entry.LpmParentPosX.HasValue)
        {
            // Peg is under LPM but we couldn't find the component — search upward
            var t = finalPeg.transform.parent;
            while (t != null && lpm == null)
            {
                lpm = t.GetComponent<Battle.PegBehaviour.LinearPegMovement>();
                t = t.parent;
            }
        }

        if (lpm != null)
        {
            var parentT = lpm.transform;
            // Only sync each parent once per heartbeat
            if (_syncedMovementParents.Add(parentT))
            {
                if (entry.LpmParentPosX.HasValue && entry.LpmParentPosY.HasValue)
                {
                    // Use host's authoritative parent position directly
                    var newParentPos = new Vector3(entry.LpmParentPosX.Value,
                        entry.LpmParentPosY.Value, parentT.position.z);
                    var rb = lpm.GetComponent<Rigidbody2D>();
                    if (rb != null)
                        rb.position = new Vector2(newParentPos.x, newParentPos.y);
                    else
                        parentT.position = newParentPos;
                }
                else
                {
                    // Fallback: calculate delta from child (legacy path)
                    var childWorldPos = finalPeg.transform.position;
                    var delta = hostPos - childWorldPos;
                    var newParentPos = parentT.position + delta;
                    newParentPos.z = parentT.position.z;
                    var rb = lpm.GetComponent<Rigidbody2D>();
                    if (rb != null)
                        rb.position = new Vector2(newParentPos.x, newParentPos.y);
                    else
                        parentT.position = newParentPos;
                }
            }
            // Skip individual peg position setting — parent handles it
            return;
        }

        // If ConvertPegToType created a new GO (bomb as child of original peg),
        // snap PARENT first so the subsequent child-set lands at the right world pos.
        // Setting child then parent double-offsets the child (parent delta propagates
        // to child world-space) — we were seeing bombs at 2*hostPos - origPos.
        if (finalPeg != originalPeg && originalPeg != null)
        {
            originalPeg.transform.position = hostPos;
            var rb2 = originalPeg.GetComponent<Rigidbody2D>();
            if (rb2 != null)
            {
                rb2.position = new Vector2(entry.PosX, entry.PosY);
                if (rb2.bodyType != RigidbodyType2D.Static)
                    rb2.velocity = Vector2.zero;
            }
        }

        // Bombs always hard-snap — they must match host exactly every heartbeat
        // per the "dumb canvas" rule. Lerp leaves visible drift that never converges.
        bool isBomb = finalPeg is Bomb;
        if (!isBomb && HasMovementComponent(finalPeg))
        {
            finalPeg.transform.position = Vector3.Lerp(
                finalPeg.transform.position, hostPos, 0.15f);
        }
        else
        {
            finalPeg.transform.position = hostPos;
            var rb = finalPeg.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.position = new Vector2(entry.PosX, entry.PosY);
                // Zero velocity on static pegs to prevent physics drift from position corrections
                if (rb.bodyType != RigidbodyType2D.Static)
                    rb.velocity = Vector2.zero;
            }
        }
    }

    private GameObject _cachedBombPrefab;

    /// <summary>
    /// Create a new Bomb on the client when the host spawned one out of thin air
    /// (bob-orb, explosive relic, etc.) and no existing peg is available to repurpose.
    /// Grabs _bombPrefab from any RegularPeg, instantiates, registers with the host GUID,
    /// and adds to PegManager._bombs. Returns null if no template is available.
    /// </summary>
    private Peg SynthesizeBomb(PegEntry entry, List<Peg> clientPegs, List<Bomb> clientBombs)
    {
        try
        {
            if (_cachedBombPrefab == null)
            {
                var prefabField = HarmonyLib.AccessTools.Field(typeof(RegularPeg), "_bombPrefab");
                if (prefabField != null)
                {
                    foreach (var p in clientPegs)
                    {
                        if (p is RegularPeg rp)
                        {
                            var candidate = prefabField.GetValue(rp) as GameObject;
                            if (candidate != null) { _cachedBombPrefab = candidate; break; }
                        }
                    }
                }
            }
            if (_cachedBombPrefab == null) return null;

            // Prefer parenting under the matching LPM row transform when the host
            // marks this bomb as living under LinearPegMovement. Without this, the
            // bomb sits under an arbitrary static parent and the heartbeat-only
            // position sync visibly teleports it; under the LPM row, the existing
            // moving-peg sync (parent-nudge + LPM tween) carries it smoothly.
            Transform parent = null;
            bool parentedUnderLpm = false;
            if (entry.HasLpm && entry.LpmParentPosX.HasValue && entry.LpmParentPosY.HasValue)
            {
                var lpms = UnityEngine.Object.FindObjectsOfType<Battle.PegBehaviour.LinearPegMovement>();
                float bestDistSq = 0.5f * 0.5f;
                foreach (var lpm in lpms)
                {
                    if (lpm == null) continue;
                    var lp = lpm.transform.position;
                    float dx = lp.x - entry.LpmParentPosX.Value;
                    float dy = lp.y - entry.LpmParentPosY.Value;
                    float d = dx * dx + dy * dy;
                    if (d < bestDistSq) { bestDistSq = d; parent = lpm.transform; }
                }
                if (parent != null) parentedUnderLpm = true;
            }
            if (parent == null)
            {
                foreach (var p in clientPegs)
                {
                    if (p != null && p.transform.parent != null) { parent = p.transform.parent; break; }
                }
            }

            var pos = new Vector3(entry.PosX, entry.PosY, 0f);
            var go = UnityEngine.Object.Instantiate(_cachedBombPrefab, pos, Quaternion.identity, parent);
            go.SetActive(true);
            var bomb = go.GetComponent<Bomb>();
            if (bomb == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }
            _pegId.Register(bomb, entry.Guid);
            if (clientBombs != null && !clientBombs.Contains(bomb))
                clientBombs.Add(bomb);
            _log.LogInfo($"[PegboardApplier] BOMB SYNTHESIZED: guid={entry.Guid} " +
                $"pos=({entry.PosX:F1},{entry.PosY:F1}) from _bombPrefab " +
                $"underLpm={parentedUnderLpm}");
            return bomb;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[PegboardApplier] SynthesizeBomb failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Build a structural index of client pegs keyed by (parent_name, localPos).
    /// Used for first-sync binding that survives non-deterministic pm.allPegs
    /// ordering. The key comes from the prefab hierarchy, so host and client
    /// produce identical keys for the same logical layout slot.
    /// May contain multiple pegs per key (collisions resolved later by iteration).
    /// </summary>
    /// <summary>
    /// Pair of structural indexes. Pos-keyed uses (parent_name, localPos) which
    /// is stable for static pegs but drifts for self-LPM pegs. Sibling-keyed
    /// uses (parent_name, sibling_index) which stays stable regardless of
    /// physics. The applier prefers sibling for HasLpm entries, pos otherwise.
    /// </summary>
    private class StructIndex
    {
        public Dictionary<string, List<Peg>> ByPos =
            new Dictionary<string, List<Peg>>(System.StringComparer.Ordinal);
        public Dictionary<string, List<Peg>> BySibling =
            new Dictionary<string, List<Peg>>(System.StringComparer.Ordinal);
    }

    private static StructIndex BuildStructIndex(
        List<Peg> clientPegs, List<Bomb> clientBombs, List<BouncerPeg> clientBouncers)
    {
        var index = new StructIndex();
        void AddTo(Dictionary<string, List<Peg>> dict, string key, Peg p)
        {
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<Peg>();
                dict[key] = list;
            }
            list.Add(p);
        }
        void Add(Peg p)
        {
            if (p == null) return;
            var parent = p.transform.parent;
            var parentName = parent != null ? parent.name : string.Empty;
            var lp = p.transform.localPosition;
            AddTo(index.ByPos, MakePosStructKey(parentName, lp.x, lp.y), p);
            AddTo(index.BySibling, MakeSiblingStructKey(parentName, p.transform.GetSiblingIndex()), p);
        }
        if (clientPegs != null) foreach (var p in clientPegs) Add(p);
        if (clientBombs != null) foreach (var b in clientBombs) Add(b);
        if (clientBouncers != null) foreach (var bo in clientBouncers) Add(bo);
        return index;
    }

    private static string MakePosStructKey(string parentName, float lx, float ly)
    {
        // Round to 3 decimals to absorb float jitter between host/client.
        return $"{parentName}|{lx:F3}|{ly:F3}";
    }

    private static string MakeSiblingStructKey(string parentName, int siblingIndex)
    {
        return $"{parentName}|#{siblingIndex}";
    }

    /// <summary>
    /// Find an unmatched client peg whose structural key matches the entry.
    /// Pops the peg out of both indices so it won't be matched again.
    ///
    /// For HasLpm entries, prefers sibling-index matching — LPM-driven pegs
    /// have drifting localPosition, so pos-matching would bind the wrong
    /// peg (that's how "the wrong pegs are moving" on the client). Sibling
    /// index is baked into the prefab and survives physics updates.
    /// </summary>
    private static Peg ResolveByStructKey(StructIndex index, PegEntry entry)
    {
        if (index == null || string.IsNullOrEmpty(entry.ParentName)) return null;

        Peg peg = null;
        if (entry.HasLpm)
        {
            peg = PopFromIndex(index.BySibling,
                MakeSiblingStructKey(entry.ParentName, entry.SiblingIndex));
            if (peg == null)
            {
                // Fallback to pos for LPM-on-ancestor pegs (localPosition is
                // still stable when LPM moves a parent row).
                peg = PopPosMatchingEntry(index.ByPos,
                    MakePosStructKey(entry.ParentName, entry.LocalPosX, entry.LocalPosY), entry);
            }
        }
        else
        {
            peg = PopPosMatchingEntry(index.ByPos,
                MakePosStructKey(entry.ParentName, entry.LocalPosX, entry.LocalPosY), entry);
            if (peg == null && entry.SiblingIndex >= 0)
            {
                // Fallback to sibling for legacy layouts where localPos drifts
                // but HasLpm wasn't flagged.
                peg = PopFromIndex(index.BySibling,
                    MakeSiblingStructKey(entry.ParentName, entry.SiblingIndex));
            }
        }

        if (peg != null)
        {
            // Remove from the other dict too so this peg can't be double-bound.
            RemovePegFromAllLists(index, peg);
        }
        return peg;
    }

    private static Peg PopFromIndex(Dictionary<string, List<Peg>> dict, string key)
    {
        if (!dict.TryGetValue(key, out var list) || list.Count == 0) return null;
        var peg = list[list.Count - 1];
        list.RemoveAt(list.Count - 1);
        return peg;
    }

    // LongPegs (and other mesh-baked flat pegs) all share the same localPosition
    // because their geometry lives in mesh vertices, not the transform. That
    // makes (parentName|localPos) a many-to-one bucket, and PopFromIndex would
    // return an arbitrary peg from the list — in MinotaurLayout2 this produced
    // a horizontal mirror of host state. Disambiguate by worldPos: pick the
    // peg in the collision list whose world position is closest to the host's
    // reported position, within a reasonable threshold.
    private static Peg PopPosMatchingEntry(
        Dictionary<string, List<Peg>> dict, string key, PegEntry entry)
    {
        if (!dict.TryGetValue(key, out var list) || list.Count == 0) return null;

        if (list.Count == 1)
        {
            var only = list[0];
            list.RemoveAt(0);
            return only;
        }

        int bestIdx = -1;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            if (p == null) continue;
            float dx = p.transform.position.x - entry.PosX;
            float dy = p.transform.position.y - entry.PosY;
            float d2 = dx * dx + dy * dy;
            if (d2 < bestDistSq)
            {
                bestDistSq = d2;
                bestIdx = i;
            }
        }

        // Threshold: 1.5 units. Same value as IndexBindPositionSane — if the
        // closest candidate is farther than this, something structural is off
        // and we should let Phase 2 (FindClosestUnmatched) handle it rather
        // than commit to a wrong bind.
        if (bestIdx < 0 || bestDistSq > 1.5f * 1.5f) return null;

        var picked = list[bestIdx];
        list.RemoveAt(bestIdx);
        return picked;
    }

    private static void RemovePegFromAllLists(StructIndex index, Peg peg)
    {
        foreach (var list in index.ByPos.Values) list.Remove(peg);
        foreach (var list in index.BySibling.Values) list.Remove(peg);
    }

    /// <summary>
    /// Guard against binding-by-index when positions wildly disagree.
    /// If neither side is near the LPM buffer position (0,1.4), require
    /// the index candidate's world position to be within 1.5 units of the
    /// host position; otherwise the list orderings are misaligned and the
    /// bind would corrupt state.
    /// </summary>
    private static bool IndexBindPositionSane(Peg indexCandidate, PegEntry entry)
    {
        float dx = indexCandidate.transform.position.x - entry.PosX;
        float dy = indexCandidate.transform.position.y - entry.PosY;
        float distSq = dx * dx + dy * dy;
        if (distSq <= 1.5f * 1.5f) return true;

        // Both sides in buffer region — accept (structural tie-break not available).
        bool hostBuffer = System.Math.Abs(entry.PosX) < 0.5f && System.Math.Abs(entry.PosY - 1.4f) < 0.5f;
        float cx = indexCandidate.transform.position.x;
        float cy = indexCandidate.transform.position.y;
        bool clientBuffer = System.Math.Abs(cx) < 0.5f && System.Math.Abs(cy - 1.4f) < 0.5f;
        return hostBuffer && clientBuffer;
    }

    /// <summary>
    /// Resolve a global index into the client's concatenated peg list.
    /// Layout matches the provider:
    /// [0 .. pegsCount-1]             = clientPegs[index]
    /// [pegsCount .. +bombsCount-1]   = clientBombs[index - pegsCount]
    /// [pegsCount+bombsCount .. ]     = clientBouncers[index - pegsCount - bombsCount]
    /// Returns null for out-of-range indices.
    /// </summary>
    private static Peg ResolveByIndex(int index, int pegsCount, int bombsCount,
        List<Peg> clientPegs, List<Bomb> clientBombs, List<BouncerPeg> clientBouncers)
    {
        if (index < 0) return null;
        if (index < pegsCount) return clientPegs[index];
        int bombIdx = index - pegsCount;
        if (bombIdx < bombsCount) return clientBombs[bombIdx];
        int bouncerIdx = bombIdx - bombsCount;
        if (clientBouncers != null && bouncerIdx < clientBouncers.Count)
            return clientBouncers[bouncerIdx];
        return null;
    }

    /// <summary>
    /// Check that the client peg's runtime type aligns with the snapshot entry.
    /// Bomb entries must map to Bomb instances, bouncer entries to BouncerPeg,
    /// regular entries to non-bomb non-bouncer pegs.
    /// </summary>
    private static bool TypeMatches(Peg peg, PegEntry entry)
    {
        bool pegIsBomb = peg is Bomb;
        bool pegIsBouncer = peg is BouncerPeg;
        return pegIsBomb == entry.IsBomb && pegIsBouncer == entry.IsBouncer;
    }

    /// <summary>
    /// Find the closest unmatched peg within the given distance threshold.
    /// Pass null for any list to skip it (type-aware matching).
    /// </summary>
    private Peg FindClosestUnmatched(PegEntry entry, List<Peg> clientPegs,
        List<Bomb> clientBombs, List<BouncerPeg> clientBouncers,
        HashSet<Peg> matched, float maxDist)
    {
        Peg closest = null;
        float closestDist = maxDist * maxDist;

        if (clientPegs != null)
        {
            foreach (var p in clientPegs)
            {
                if (p == null || matched.Contains(p)) continue;
                float dx = p.transform.position.x - entry.PosX;
                float dy = p.transform.position.y - entry.PosY;
                float dist = dx * dx + dy * dy;
                if (dist < closestDist) { closestDist = dist; closest = p; }
            }
        }
        if (clientBombs != null)
        {
            foreach (var b in clientBombs)
            {
                if (b == null || matched.Contains(b)) continue;
                float dx = b.transform.position.x - entry.PosX;
                float dy = b.transform.position.y - entry.PosY;
                float dist = dx * dx + dy * dy;
                if (dist < closestDist) { closestDist = dist; closest = b; }
            }
        }
        if (clientBouncers != null)
        {
            foreach (var bo in clientBouncers)
            {
                if (bo == null || matched.Contains(bo)) continue;
                float dx = bo.transform.position.x - entry.PosX;
                float dy = bo.transform.position.y - entry.PosY;
                float dist = dx * dx + dy * dy;
                if (dist < closestDist) { closestDist = dist; closest = bo; }
            }
        }
        return closest;
    }

    /// <summary>
    /// Apply host state (type, cleared, destroyed, slime, coins, bomb fuse) to a matched client peg.
    /// </summary>
    private void ApplyPegState(Peg peg, PegEntry entry, List<Bomb> clientBombs,
        ref int typeChanged, ref int destroyed, ref int reactivated, ref int cleared)
    {
        // Register with host GUID
        if (!string.IsNullOrEmpty(entry.Guid))
            _pegId.Register(peg, entry.Guid);

        bool clientPopped = false;
        try { clientPopped = peg.IsDisabled(); } catch { }

        // Handle cleared/popped pegs.
        // The host keeps popped pegs visible as the "destroyed dot" sprite until
        // BattleController.RemoveClearedPegs() runs at end of battle / nav failure.
        // Previously we called RemoveIfCleared() here, which fades alpha→0 and
        // Disables the GameObject — the client's pegs vanished after each shot
        // while the host's stayed persistent, making the client unable to see
        // the board for aiming. Match host behavior: pop the peg (collider off,
        // scale tween → dot sprite) but do NOT fade it. End-of-battle fade
        // arrives naturally when the host sets IsDestroyed=true in a later heartbeat.
        if (entry.IsCleared && !clientPopped)
        {
            try
            {
                if (peg is LongPeg longPegCleared)
                {
                    // Host has disabled this LongPeg's collider (SetActiveStatus(false)
                    // ran). Mirror host visually: ensure gray hit state is applied
                    // (in case we missed the PegActivatedEvent) and fade out via
                    // RemoveIfCleared, which handles collider/trigger/poppedPegCollider
                    // state and the alpha-fade tween. Calling PegActivated here would
                    // run relic logic and could NRE on client where relicManager state
                    // isn't authoritative.
                    LongPegVisualHelper.ApplyHitVisual(longPegCleared);
                    try { longPegCleared.RemoveIfCleared(); } catch { }
                }
                else
                {
                    peg.PegActivated(playAudio: false, forcePop: true);
                }

                cleared++;
            }
            catch { }
        }

        // Handle destroyed pegs
        if (entry.IsDestroyed)
        {
            if (peg.pegType != Peg.PegType.DESTROYED)
            {
                if (peg.gameObject.activeSelf)
                {
                    try { peg.DestroyPeg(peg.pegType); }
                    catch { peg.gameObject.SetActive(false); }
                }
                else
                {
                    // Already inactive (e.g. enemy-lobbed bombs that detonated) —
                    // just set the type so the state matches the host.
                    peg.pegType = Peg.PegType.DESTROYED;
                }
                destroyed++;
            }
            return;
        }

        // Reactivate if host says peg is alive but client has it disabled/popped
        if (!entry.IsCleared && !entry.IsDestroyed)
        {
            var clearedField = HarmonyLib.AccessTools.Field(typeof(Peg), "_cleared");

            if (!peg.gameObject.activeSelf || peg.pegType == Peg.PegType.DESTROYED || clientPopped)
            {
                DG.Tweening.DOTween.Kill(peg.gameObject);

                // Activate all parents in the hierarchy first — bombs under
                // inactive parent containers (RotatingPegCircle, pegboard sub-groups)
                // will have activeSelf=true but activeInHierarchy=false.
                EnsureParentChainActive(peg.gameObject);

                clearedField?.SetValue(peg, entry.WasPreviouslyCleared);
                peg.gameObject.SetActive(true);

                // Peg.Reset() early-returns for "indestructible" types (DESTROYED,
                // DULL, BOUNCER) without re-enabling colliders. If a peg is stuck
                // as DESTROYED on the client (prior heartbeat marked IsDestroyed)
                // but the host now wants it alive, flip the type to REGULAR first
                // so Reset actually runs SwitchToRegularColliders / SetActiveStatus.
                // Otherwise IsDisabled() keeps returning true and every heartbeat
                // loops through reactivation with no effect.
                if (peg.pegType == Peg.PegType.DESTROYED)
                    peg.pegType = Peg.PegType.REGULAR;

                try { peg.Reset(false); } catch { }

                ForceRendererVisible(peg);

                reactivated++;
            }
            else
            {
                bool clientCleared = (bool)(clearedField?.GetValue(peg) ?? false);
                if (clientCleared != entry.WasPreviouslyCleared)
                {
                    clearedField?.SetValue(peg, entry.WasPreviouslyCleared);
                    try { peg.Reset(false); } catch { }
                }
            }
        }

        // LongPeg-specific: reconcile half-hit "gray" state with host.
        //   - entry.IsLongPegHit == true  → host says peg is in gray half-hit state
        //     (collider still enabled, _hit=true, _colors.Hit material). Ensure the
        //     client visual matches even if we missed PegActivatedEvent.
        //   - entry.IsLongPegHit == false → host says peg is fresh (not hit). If the
        //     client's peg has stale _hit/_beingHit (e.g. event was applied locally
        //     but host has since cleared the state via Reset/HardReset on the peg
        //     across turn boundaries), HardReset to normalize.
        // HardReset() resets _hit/_beingHit/_numBounces/_timeHit, calls
        // SetActiveStatus(true) (re-enables collider, restores active material), and
        // re-runs ConvertPegToType(pegType) — so if we pre-set pegType to the target
        // type, HardReset normalizes everything in one shot.
        if (peg is LongPeg longPeg && !entry.IsCleared && !entry.IsDestroyed)
        {
            var hitField = HarmonyLib.AccessTools.Field(typeof(LongPeg), "_hit");
            var beingHitField = HarmonyLib.AccessTools.Field(typeof(LongPeg), "_beingHit");
            bool isHit = (bool)(hitField?.GetValue(peg) ?? false);
            bool beingHit = (bool)(beingHitField?.GetValue(peg) ?? false);

            if (entry.IsLongPegHit)
            {
                // Host says peg should look gray. Apply visual if client doesn't
                // already have it. Don't HardReset — that would erase the gray state.
                if (!isHit)
                {
                    LongPegVisualHelper.ApplyHitVisual(longPeg);
                    _log.LogInfo($"[PegboardApplier] LongPeg gray-state recovered (missed event): " +
                        $"guid={entry.Guid} at ({entry.PosX:F1},{entry.PosY:F1})");
                }
            }
            else if (isHit || beingHit)
            {
                var targetType = (Peg.PegType)entry.PegType;
                if (targetType != Peg.PegType.DESTROYED)
                    peg.pegType = targetType;
                try { longPeg.HardReset(); } catch { }
                _log.LogInfo($"[PegboardApplier] LongPeg hit-state normalized: guid={entry.Guid} " +
                    $"wasHit={isHit} wasBeingHit={beingHit} → {targetType} at " +
                    $"({entry.PosX:F1},{entry.PosY:F1})");
            }
        }

        // Force peg type to match host. Bouncers (rubber) DO get synced — when the host
        // shuffles peg types (e.g. random peg field, shuffle stages), the bouncer's peg
        // slot on the client must reflect host state. Without this, rubber pegs stay
        // pinned to whatever positions the client started with.
        {
            var targetType = (Peg.PegType)entry.PegType;
            if (peg.pegType != targetType)
            {
                try
                {
                    if (peg.SupportsPegType(targetType))
                    {
                        var result = peg.ConvertPegToType(targetType);
                        if (targetType == Peg.PegType.BOMB && result != null && result != peg.gameObject)
                        {
                            var newBomb = result.GetComponent<Bomb>();
                            if (newBomb != null)
                            {
                                if (!string.IsNullOrEmpty(entry.Guid))
                                    _pegId.Register(newBomb, entry.Guid);

                                if (clientBombs != null && !clientBombs.Contains(newBomb))
                                    clientBombs.Add(newBomb);
                            }
                        }
                    }
                    else
                    {
                        peg.pegType = targetType;
                    }
                }
                catch
                {
                    peg.pegType = targetType;
                }
                typeChanged++;
            }

            // Safety net: ConvertPegToType can silently bail (SupportsPegType=false,
            // RESET→CRIT clobber rejection, renderer null, etc.) leaving pegType
            // correct but the sprite still showing the previous visual. Force the
            // special sprite via reflection so CRIT/RESET always render correctly.
            ForceSpecialPegSpriteIfNeeded(peg, targetType);
        }

        // After type conversion, if the peg is in "previously cleared" state,
        // re-apply the dot sprite — BUT only for plain REGULAR pegs. Special
        // types (CRIT/RESET/VINE/SPINFECTION/etc.) keep their special sprite.
        if (entry.WasPreviouslyCleared && !entry.IsCleared && !entry.IsDestroyed
            && (Peg.PegType)entry.PegType == Peg.PegType.REGULAR)
        {
            if (peg is RegularPeg)
            {
                var rendererField = HarmonyLib.AccessTools.Field(typeof(RegularPeg), "_renderer");
                var spriteField = HarmonyLib.AccessTools.Field(typeof(RegularPeg), "_previouslyClearedSprite");
                var renderer = rendererField?.GetValue(peg) as SpriteRenderer;
                var sprite = spriteField?.GetValue(peg) as Sprite;
                if (renderer != null && sprite != null)
                    renderer.sprite = sprite;
            }
        }

        // Apply slime type
        var targetSlime = (Peg.SlimeType)entry.SlimeType;
        if (peg.slimeType != targetSlime)
        {
            try
            {
                if (targetSlime == Peg.SlimeType.None)
                    peg.RemoveSlime(true);
                else
                    peg.ApplySlimeToPeg(targetSlime);
            }
            catch { }
        }

        // Sync gold coins: add missing or collect consumed
        {
            int currentCoins = peg.NumCoins();
            if (currentCoins < entry.CoinCount)
            {
                for (int c = currentCoins; c < entry.CoinCount; c++)
                {
                    try { peg.AddCoin(false); } catch { }
                }
            }
            else if (currentCoins > entry.CoinCount && currentCoins > 0)
            {
                // Host collected coins (peg was hit) — remove visual on client
                try
                {
                    var overlayField = HarmonyLib.AccessTools.Field(typeof(Peg), "PegCoinOverlayInstance");
                    var overlay = overlayField?.GetValue(peg) as Battle.PegBehaviour.PegCoinOverlay;
                    if (overlay != null)
                    {
                        int toCollect = currentCoins - entry.CoinCount;
                        overlay.CollectCoins(toCollect);
                    }
                }
                catch { }
            }
        }

        // Sync buff amount
        if (peg.buffAmount != entry.BuffAmount)
        {
            int diff = entry.BuffAmount - peg.buffAmount;
            if (diff != 0)
            {
                try { peg.AddBuff(diff); } catch { }
            }
        }

        // Sync bomb hit count
        if (entry.IsBomb && peg is Bomb bomb)
        {
            if (bomb.HitCount != entry.HitCount)
            {
                bomb.HitCount = entry.HitCount;
                try
                {
                    var animator = bomb.GetComponent<Animator>();
                    animator?.SetInteger("NumHits", entry.HitCount);
                }
                catch { }
            }
        }

        // Sync shield overlay
        SyncShieldState(peg, entry);
    }

    private void SyncShieldState(Peg peg, PegEntry entry)
    {
        try
        {
            var overlayField = HarmonyLib.AccessTools.Field(typeof(Peg), "PegShieldOverlayInstance");
            var shieldedField = HarmonyLib.AccessTools.Field(typeof(Peg), "_shielded");
            var overlay = overlayField?.GetValue(peg) as Battle.PegBehaviour.PegShieldOverlay;

            if (entry.IsShielded)
            {
                // Host says this peg should be shielded
                if (!peg.shielded)
                {
                    // Apply shielding if not already shielded
                    try { peg.ApplyShielding(claimed: false, startupShield: false); }
                    catch { }
                    overlay = overlayField?.GetValue(peg) as Battle.PegBehaviour.PegShieldOverlay;
                }

                if (overlay != null)
                {
                    overlay.hitCount = entry.ShieldHitCount;
                    overlay.hitLimit = entry.ShieldHitLimit;
                    // Update the animator to show correct visual state
                    try
                    {
                        var anim = overlay.GetComponent<UnityEngine.Animator>();
                        anim?.SetInteger(UnityEngine.Animator.StringToHash("HitCount"), entry.ShieldHitCount);
                        // Hide if broken
                        var rend = overlay.GetComponent<UnityEngine.SpriteRenderer>();
                        if (rend != null)
                            rend.enabled = entry.ShieldHitCount < entry.ShieldHitLimit;
                    }
                    catch { }
                }
            }
            else if (peg.shielded)
            {
                // Host says no shield but client has one — remove it
                if (overlay != null)
                {
                    overlay.hitCount = overlay.hitLimit;
                    overlay.gameObject.SetActive(false);
                }
                shieldedField?.SetValue(peg, false);
            }
        }
        catch { }
    }

    private static bool HasMovementComponent(Peg peg)
    {
        if (peg == null) return false;
        var go = peg.gameObject;

        // Check self and direct parent only for LinearPegMovement (not distant ancestors)
        if (go.GetComponent<Battle.PegBehaviour.LinearPegMovement>() != null) return true;
        if (go.transform.parent != null &&
            go.transform.parent.GetComponent<Battle.PegBehaviour.LinearPegMovement>() != null) return true;

        return go.GetComponent<Battle.PegBehaviour.PegMoveAndReturn>() != null
            || go.GetComponent<Battle.PegBehaviour.PegSquareMovement>() != null
            || go.GetComponent<Battle.PegBehaviour.PegSplineFollow>() != null
            || go.GetComponentInParent<Battle.PegBehaviour.RotatingPegCircle>() != null;
    }

    /// <summary>
    /// Walk up the transform hierarchy and activate any inactive parents.
    /// Bombs under inactive containers (RotatingPegCircle, pegboard sub-groups)
    /// can have activeSelf=true but activeInHierarchy=false, making them invisible.
    /// </summary>
    private void EnsureParentChainActive(GameObject go)
    {
        var parent = go.transform.parent;
        while (parent != null)
        {
            if (!parent.gameObject.activeSelf)
            {
                _log.LogInfo($"[PegboardApplier] Activating inactive parent '{parent.name}' for peg at ({go.transform.position.x:F1},{go.transform.position.y:F1})");
                parent.gameObject.SetActive(true);
            }
            parent = parent.parent;
        }
    }

    private void ForceSpecialPegSpriteIfNeeded(Peg peg, Peg.PegType targetType)
    {
        if (targetType != Peg.PegType.CRIT && targetType != Peg.PegType.RESET) return;
        try
        {
            if (peg is RegularPeg)
            {
                var rendererField = HarmonyLib.AccessTools.Field(typeof(RegularPeg), "_renderer");
                var renderer = rendererField?.GetValue(peg) as SpriteRenderer;
                if (renderer == null) return;

                var spriteFieldName = targetType == Peg.PegType.CRIT ? "_critSprite" : "_resetSprite";
                var spriteField = HarmonyLib.AccessTools.Field(typeof(RegularPeg), spriteFieldName);
                var sprite = spriteField?.GetValue(peg) as Sprite;
                if (sprite == null || renderer.sprite == sprite) return;

                renderer.sprite = sprite;

                var colliderField = HarmonyLib.AccessTools.Field(typeof(RegularPeg), "_specialPegCollider");
                var coll = colliderField?.GetValue(peg) as Collider2D;
                if (coll != null) coll.enabled = true;
            }
            else if (peg is LongPeg)
            {
                var spriteFieldName = targetType == Peg.PegType.CRIT ? "_critSprite" : "_resetSprite";
                var spriteField = HarmonyLib.AccessTools.Field(typeof(LongPeg), spriteFieldName);
                var sprite = spriteField?.GetValue(peg) as Sprite;
                if (sprite == null) return;

                var overlayField = HarmonyLib.AccessTools.Field(typeof(LongPeg), "_resetOrCritSprite");
                var overlay = overlayField?.GetValue(peg) as SpriteRenderer;
                if (overlay == null || overlay.sprite == sprite) return;

                overlay.sprite = sprite;
                overlay.enabled = true;

                var holderField = HarmonyLib.AccessTools.Field(typeof(LongPeg), "_resetAndCritSpriteHolder");
                var holder = holderField?.GetValue(peg) as GameObject;
                if (holder != null) holder.SetActive(true);
            }
        }
        catch { }
    }

    private void ForceRendererVisible(Peg peg)
    {
        try
        {
            if (peg is RegularPeg)
            {
                var rendererField = HarmonyLib.AccessTools.Field(typeof(RegularPeg), "_renderer");
                var renderer = rendererField?.GetValue(peg) as SpriteRenderer;
                if (renderer != null)
                {
                    var c = renderer.color;
                    if (c.a < 1f) renderer.color = new Color(c.r, c.g, c.b, 1f);
                }
            }
            else if (peg is LongPeg)
            {
                var rendererField = HarmonyLib.AccessTools.Field(typeof(LongPeg), "_renderer");
                var renderer = rendererField?.GetValue(peg) as MeshRenderer;
                if (renderer?.material != null)
                {
                    var c = renderer.material.color;
                    if (c.a < 1f) renderer.material.color = new Color(c.r, c.g, c.b, 1f);
                }
            }
            else if (peg is BouncerPeg)
            {
                // BouncerPeg stores its renderer in a private _renderer field (SpriteRenderer)
                var renderer = peg.GetComponentInChildren<SpriteRenderer>();
                if (renderer != null)
                {
                    var c = renderer.color;
                    if (c.a < 1f) renderer.color = new Color(c.r, c.g, c.b, 1f);
                }
            }
        }
        catch { }
    }

    private void LogActualPegState(List<Peg> pegs, List<Bomb> bombs, List<BouncerPeg> bouncers)
    {
        int active = 0, crits = 0, bombCount = 0, resets = 0, regular = 0, bouncerCount = 0;
        foreach (var peg in pegs)
        {
            if (peg == null || !peg.gameObject.activeSelf || peg.pegType == Peg.PegType.DESTROYED) continue;
            bool disabled = false;
            try { disabled = peg.IsDisabled(); } catch { }
            if (disabled) continue;
            active++;
            var pt = (int)peg.pegType;
            if ((pt & 0x2) != 0) crits++;
            else if ((pt & 0x8) != 0) resets++;
            else if ((pt & 0x1) != 0) regular++;
        }
        if (bombs != null)
        {
            foreach (var bomb in bombs)
            {
                if (bomb == null || !bomb.gameObject.activeSelf || bomb.pegType == Peg.PegType.DESTROYED) continue;
                bool disabled = false;
                try { disabled = bomb.IsDisabled(); } catch { }
                if (disabled) continue;
                active++;
                bombCount++;
            }
        }
        if (bouncers != null)
        {
            foreach (var bouncer in bouncers)
            {
                if (bouncer == null || !bouncer.gameObject.activeSelf || bouncer.pegType == Peg.PegType.DESTROYED) continue;
                bool disabled = false;
                try { disabled = bouncer.IsDisabled(); } catch { }
                if (disabled) continue;
                active++;
                bouncerCount++;
            }
        }
        _log.LogInfo($"[PegboardApplier] CLIENT ACTUAL: {active} active pegs " +
            $"(regular={regular}, crit={crits}, bomb={bombCount}, reset={resets}, bouncer={bouncerCount})");
    }

    /// <summary>
    /// Create/destroy bramball vine visuals on the client to match the host snapshot.
    /// Uses BattleController.CreateBramballVine() for proper prefab visuals,
    /// then disables colliders so no gameplay interaction occurs on the client.
    /// </summary>
    private void SyncVines(PegboardStateSnapshot snapshot, BattleController bc)
    {
        try
        {
            // Clean up stale entries (destroyed GameObjects from scene changes)
            var stale = new List<string>();
            foreach (var kvp in _clientVines)
            {
                if (kvp.Value == null) stale.Add(kvp.Key);
            }
            foreach (var key in stale) _clientVines.Remove(key);

            var hostVineKeys = new HashSet<string>();

            if (snapshot.Vines != null && snapshot.Vines.Count > 0 && bc != null)
            {
                foreach (var vine in snapshot.Vines)
                {
                    if (string.IsNullOrEmpty(vine.Peg1Guid) || string.IsNullOrEmpty(vine.Peg2Guid))
                        continue;

                    var key = VineKey(vine.Peg1Guid, vine.Peg2Guid);
                    hostVineKeys.Add(key);

                    if (_clientVines.ContainsKey(key)) continue;

                    var peg1 = _pegId.Find(vine.Peg1Guid);
                    var peg2 = _pegId.Find(vine.Peg2Guid);
                    if (peg1 == null || peg2 == null)
                    {
                        _log.LogWarning($"[PegboardApplier] Vine peg not found: " +
                            $"peg1={vine.Peg1Guid}({peg1 != null}) peg2={vine.Peg2Guid}({peg2 != null})");
                        continue;
                    }

                    try
                    {
                        var vineGo = bc.CreateBramballVine();
                        var lr = vineGo.GetComponent<LineRenderer>();

                        Vector3 pos1 = peg1 is LongPeg ? peg1.GetCenterOfPeg() : peg1.transform.position;
                        Vector3 pos2 = peg2 is LongPeg ? peg2.GetCenterOfPeg() : peg2.transform.position;

                        if (lr != null)
                        {
                            lr.SetPosition(0, pos1);
                            lr.SetPosition(1, pos2);

                            // Apply a vine material from the prefab's material list
                            var vineComp = vineGo.GetComponent<Battle.Pachinko.Obstacles.PegBoardBramballVine>();
                            if (vineComp != null && vineComp.vineMaterials != null && vineComp.vineMaterials.Count > 0)
                            {
                                int matIdx = UnityEngine.Random.Range(0, vineComp.vineMaterials.Count);
                                lr.material = vineComp.vineMaterials[matIdx];
                            }
                        }

                        vineGo.transform.position = (pos1 + pos2) / 2f;
                        Vector3 to = pos2 - pos1;
                        vineGo.transform.eulerAngles = new Vector3(0f, 0f,
                            Vector3.SignedAngle(Vector3.up, to, Vector3.forward));

                        // Disable ALL colliders — client vines are visual only
                        foreach (var col in vineGo.GetComponentsInChildren<Collider2D>(true))
                            col.enabled = false;

                        _clientVines[key] = vineGo;
                        _log.LogInfo($"[PegboardApplier] Created vine between {vine.Peg1Guid} and {vine.Peg2Guid}");
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning($"[PegboardApplier] Failed to create vine: {ex.Message}");
                    }
                }
            }

            // Remove client vines not present in host snapshot
            var toRemove = new List<string>();
            foreach (var kvp in _clientVines)
            {
                if (!hostVineKeys.Contains(kvp.Key))
                {
                    if (kvp.Value != null)
                        UnityEngine.Object.Destroy(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var key in toRemove)
                _clientVines.Remove(key);

            if ((snapshot.Vines?.Count ?? 0) > 0 || _clientVines.Count > 0)
            {
                _log.LogInfo($"[PegboardApplier] Vine sync: host={snapshot.Vines?.Count ?? 0} " +
                    $"client={_clientVines.Count} removed={toRemove.Count}");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[PegboardApplier] SyncVines failed: {ex.Message}");
        }
    }
}
