using System;
using System.Collections.Generic;
using Battle;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

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

            int guidMatched = 0, posMatched = 0, repositioned = 0, typeChanged = 0,
                destroyed = 0, reactivated = 0, missed = 0, guidTypeInvalid = 0;
            var matchedPegs = new HashSet<Peg>();

            var unmatchedEntries = new List<PegEntry>();

            // ===== PHASE 1 & 2: GUID match (type-validated), then type-aware position match =====
            foreach (var entry in snapshot.Pegs)
            {
                Peg peg = null;

                // Phase 1: GUID match with type validation
                if (!string.IsNullOrEmpty(entry.Guid))
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
                // Prefer same-type: bombs→bombs, bouncers→bouncers, regulars→regulars
                if (peg == null)
                {
                    if (entry.IsBomb)
                        peg = FindClosestUnmatched(entry, null, clientBombs, null, matchedPegs, 3f);
                    else if (entry.IsBouncer)
                        peg = FindClosestUnmatched(entry, null, null, clientBouncers, matchedPegs, 3f);
                    else
                        peg = FindClosestUnmatched(entry, clientPegs, null, null, matchedPegs, 1f);

                    // Cross-type fallback only if same-type failed
                    if (peg == null)
                        peg = FindClosestUnmatched(entry, clientPegs, clientBombs, clientBouncers, matchedPegs, 2f);

                    if (peg != null)
                        posMatched++;
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
                        ApplyPegState(peg, entry, clientBombs, ref typeChanged, ref destroyed, ref reactivated);
                    }

                    // Log bomb entry matching details
                    if (entry.IsBomb)
                    {
                        bool pegActive = peg.gameObject.activeSelf;
                        bool pegDisabled = false;
                        try { pegDisabled = peg.IsDisabled(); } catch { }
                        _log.LogInfo($"[PegboardApplier] BOMB MATCH: guid={entry.Guid} " +
                            $"hostPos=({entry.PosX:F1},{entry.PosY:F1}) " +
                            $"clientPos=({peg.transform.position.x:F1},{peg.transform.position.y:F1}) " +
                            $"active={pegActive} disabled={pegDisabled} type={peg.pegType} " +
                            $"entry(cleared={entry.IsCleared},destroyed={entry.IsDestroyed},hits={entry.HitCount})");
                    }

                    SyncPegPosition(peg, entry);
                }
                else
                {
                    unmatchedEntries.Add(entry);
                }
            }

            // ===== PHASE 3: Reposition unmatched client pegs to host positions =====
            if (unmatchedEntries.Count > 0)
            {
                var availablePegs = new List<Peg>();
                foreach (var p in clientPegs)
                {
                    if (p != null && !matchedPegs.Contains(p))
                        availablePegs.Add(p);
                }
                if (clientBombs != null)
                {
                    foreach (var b in clientBombs)
                    {
                        if (b != null && !matchedPegs.Contains(b))
                            availablePegs.Add(b);
                    }
                }
                if (clientBouncers != null)
                {
                    foreach (var bo in clientBouncers)
                    {
                        if (bo != null && !matchedPegs.Contains(bo))
                            availablePegs.Add(bo);
                    }
                }

                Peg templatePeg = null;
                if (clientPegs.Count > 0)
                    templatePeg = clientPegs[0];

                foreach (var entry in unmatchedEntries)
                {
                    Peg peg;
                    if (availablePegs.Count > 0)
                    {
                        peg = availablePegs[availablePegs.Count - 1];
                        availablePegs.RemoveAt(availablePegs.Count - 1);
                        repositioned++;
                    }
                    else if (templatePeg != null)
                    {
                        var clone = UnityEngine.Object.Instantiate(templatePeg, templatePeg.transform.parent);
                        clone.gameObject.SetActive(true);
                        peg = clone;
                        try { pm.AddPeg(peg); } catch { clientPegs.Add(peg); }
                        repositioned++;
                    }
                    else
                    {
                        missed++;
                        continue;
                    }

                    peg.transform.position = new Vector3(entry.PosX, entry.PosY, peg.transform.position.z);
                    matchedPegs.Add(peg);
                    ApplyPegState(peg, entry, clientBombs, ref typeChanged, ref destroyed, ref reactivated);
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
            if (clientBombs != null)
            {
                foreach (var bomb in clientBombs)
                {
                    if (bomb != null && bomb.gameObject.activeSelf && !matchedPegs.Contains(bomb)
                        && !matchedParents.Contains(bomb.transform))
                    {
                        bomb.gameObject.SetActive(false);
                        extrasRemoved++;
                    }
                }
            }
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
            _log.LogInfo($"[PegboardApplier] GUIDMatched={guidMatched}, PosMatched={posMatched}, " +
                $"Repositioned={repositioned}, TypeChanged={typeChanged}, Destroyed={destroyed}, " +
                $"Reactivated={reactivated}, Missed={missed}, GUIDTypeInvalid={guidTypeInvalid}, " +
                $"ExtrasRemoved={extrasRemoved} " +
                $"(host={snapshot.TotalPegCount}, client={totalClient}, " +
                $"crit={snapshot.CritPegCount}, bomb={snapshot.BombPegCount}, " +
                $"reset={snapshot.ResetPegCount}, bouncer={snapshot.BouncerPegCount}, " +
                $"registry={_pegId.Count})");

            // Sync bramball vines
            SyncVines(snapshot, bc);

            LogActualPegState(clientPegs, clientBombs, clientBouncers);

            // Dump all client bombs for debugging
            if (clientBombs != null)
            {
                for (int i = 0; i < clientBombs.Count; i++)
                {
                    var b = clientBombs[i];
                    if (b == null) { _log.LogInfo($"[PegboardApplier] CLIENT_BOMB[{i}] NULL"); continue; }
                    bool dis = false;
                    try { dis = b.IsDisabled(); } catch { }
                    var guid = _pegId.GetGuid(b) ?? "none";
                    var parentInfo = "";
                    if (dis && b.gameObject.activeSelf)
                    {
                        // activeSelf=true but activeInHierarchy=false means parent is inactive
                        var p = b.transform.parent;
                        while (p != null)
                        {
                            if (!p.gameObject.activeSelf)
                            {
                                parentInfo = $" inactiveParent='{p.name}'";
                                break;
                            }
                            p = p.parent;
                        }
                    }
                    _log.LogInfo($"[PegboardApplier] CLIENT_BOMB[{i}] guid={guid} " +
                        $"pos=({b.transform.position.x:F1},{b.transform.position.y:F1}) " +
                        $"type={b.pegType} active={b.gameObject.activeSelf} disabled={dis} hits={b.HitCount}{parentInfo}");
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

        if (HasMovementComponent(finalPeg))
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

        // If ConvertPegToType created a new GO, also snap the original (now parent)
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
        ref int typeChanged, ref int destroyed, ref int reactivated)
    {
        // Register with host GUID
        if (!string.IsNullOrEmpty(entry.Guid))
            _pegId.Register(peg, entry.Guid);

        bool clientPopped = false;
        try { clientPopped = peg.IsDisabled(); } catch { }

        // Handle cleared/popped pegs
        if (entry.IsCleared && !clientPopped)
        {
            try
            {
                peg.PegActivated(playAudio: false, forcePop: true);

                // Force dot sprite so the fade-out shows the small dot indicator
                if (peg is RegularPeg)
                {
                    var renderer = HarmonyLib.AccessTools.Field(typeof(RegularPeg), "_renderer")
                        ?.GetValue(peg) as SpriteRenderer;
                    var sprite = HarmonyLib.AccessTools.Field(typeof(RegularPeg), "_previouslyClearedSprite")
                        ?.GetValue(peg) as Sprite;
                    if (renderer != null && sprite != null)
                        renderer.sprite = sprite;
                }

                peg.RemoveIfCleared();
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

        // Force peg type to match host (skip for bouncers — they don't support conversion)
        if (!(peg is BouncerPeg))
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
        }

        // After type conversion, if the peg is in "previously cleared" state,
        // re-apply the dot sprite.
        if (entry.WasPreviouslyCleared && !entry.IsCleared && !entry.IsDestroyed)
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
