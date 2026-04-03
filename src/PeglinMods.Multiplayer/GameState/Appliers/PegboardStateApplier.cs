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

    public PegboardStateApplier(ManualLogSource log, PegIdentifier pegId)
    {
        _log = log;
        _pegId = pegId;
    }

    public void Apply(PegboardStateSnapshot snapshot)
    {
        try
        {
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
            int extrasRemoved = 0;
            foreach (var peg in clientPegs)
            {
                if (peg != null && peg.gameObject.activeSelf && !matchedPegs.Contains(peg))
                {
                    peg.gameObject.SetActive(false);
                    extrasRemoved++;
                }
            }
            if (clientBombs != null)
            {
                foreach (var bomb in clientBombs)
                {
                    if (bomb != null && bomb.gameObject.activeSelf && !matchedPegs.Contains(bomb))
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
                    if (bouncer != null && bouncer.gameObject.activeSelf && !matchedPegs.Contains(bouncer))
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
    /// Hard-snaps static pegs; soft-lerps moving pegs.
    /// </summary>
    private void SyncPegPosition(Peg originalPeg, PegEntry entry)
    {
        var finalPeg = !string.IsNullOrEmpty(entry.Guid) ? _pegId.Find(entry.Guid) : null;
        if (finalPeg == null) finalPeg = originalPeg;

        var hostPos = new Vector3(entry.PosX, entry.PosY, finalPeg.transform.position.z);

        if (HasMovementComponent(finalPeg))
        {
            finalPeg.transform.position = Vector3.Lerp(
                finalPeg.transform.position, hostPos, 0.15f);
        }
        else
        {
            finalPeg.transform.position = hostPos;
            var rb = finalPeg.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position = new Vector2(entry.PosX, entry.PosY);
        }

        // If ConvertPegToType created a new GO, also snap the original (now parent)
        if (finalPeg != originalPeg && originalPeg != null)
        {
            originalPeg.transform.position = hostPos;
            var rb2 = originalPeg.GetComponent<Rigidbody2D>();
            if (rb2 != null) rb2.position = new Vector2(entry.PosX, entry.PosY);
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
            if (peg.gameObject.activeSelf && peg.pegType != Peg.PegType.DESTROYED)
            {
                try { peg.DestroyPeg(peg.pegType); }
                catch { peg.gameObject.SetActive(false); }
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

        // Apply gold coins
        if (entry.CoinCount > 0)
        {
            int currentCoins = peg.NumCoins();
            for (int c = currentCoins; c < entry.CoinCount; c++)
            {
                try { peg.AddCoin(false); } catch { }
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
    }

    private static bool HasMovementComponent(Peg peg)
    {
        if (peg == null) return false;
        var go = peg.gameObject;

        return go.GetComponent<Battle.PegBehaviour.LinearPegMovement>() != null
            || go.GetComponent<Battle.PegBehaviour.PegMoveAndReturn>() != null
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
}
