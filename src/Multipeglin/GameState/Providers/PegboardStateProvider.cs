using System;
using Battle;
using Battle.PegBehaviour;
using BepInEx.Logging;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Utility;

namespace Multipeglin.GameState.Providers;

public class PegboardStateProvider : IGameStateProvider<PegboardStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly PegIdentifier _pegId;

    // Last per-heartbeat composition signature — used to suppress identical
    // capture-summary log lines.
    private (int total, int crit, int bomb, int reset, int bouncer, int registry,
        int bombsListCount, int allPegsBombCount, int blackHoles, int splineGens) _lastCaptureSig;

    public PegboardStateProvider(ManualLogSource log, PegIdentifier pegId)
    {
        _log = log;
        _pegId = pegId;
    }

    public PegboardStateSnapshot Capture()
    {
        try
        {
            var snapshot = new PegboardStateSnapshot();

            // Get pegs from PegManager._allPegs — the authoritative list.
            // FindObjectsOfType<Peg> finds orphaned pre-instanced duplicates.
            // PegManager is a plain C# class (not MonoBehaviour), accessed via BattleController.
            var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
            var pm = bc?.pegManager;
            if (pm == null || pm.allPegs == null)
            {
                _log.LogInfo("[PegProvider] No PegManager or allPegs list found");
                return snapshot;
            }

            // Capture regular pegs from allPegs
            // NOTE: Bomb instances can end up in allPegs (pre-placed in layout, or added
            // via HandlePegAdded for non-BOMB types). Detect them by checking `peg is Bomb`.
            var pegs = pm.allPegs;
            var allPegsBombCount = 0;
            for (var i = 0; i < pegs.Count; i++)
            {
                var peg = pegs[i];
                if (peg == null)
                {
                    continue;
                }

                var isBombInstance = peg is Bomb;
                if (isBombInstance)
                {
                    allPegsBombCount++;
                }

                var guid = _pegId.GetOrAssignGuid(peg);
                var pt = (int)peg.pegType;
                // activeInHierarchy: catches pegs whose parent group is toggled off
                // (Spirit of Radia's PegLayoutAlternator does this). Without this,
                // such pegs read as alive on the host and the client's applier
                // force-activates their parent chain — making the inactive phase's
                // pegs appear on the client during the countdown.
                var parentHidden = peg.gameObject.activeSelf && !peg.gameObject.activeInHierarchy;
                var destroyed = !peg.gameObject.activeInHierarchy || (pt & 0x20) != 0;
                // Use IsDisabled() to check if peg is actually popped (collider disabled).
                // After Reset(), _cleared stays true but colliders are re-enabled —
                // so peg.Cleared is NOT reliable for "is this peg functionally popped."
                var cleared = false;
                var wasPreviouslyCleared = false;
                var isLongPegHit = false;
                if (!destroyed)
                {
                    try
                    {
                        cleared = peg.IsDisabled();
                    }
                    catch
                    {
                    }
                    // LongPeg-specific: capture the _hit flag (host's "half-hit gray
                    // state during shot") separately from cleared. When _hit=true and
                    // collider is still enabled, the client should render gray without
                    // popping. When the host eventually disables the collider via
                    // SetActiveStatus(false), IsDisabled() flips to true and IsCleared
                    // takes over → client fades the peg.
                    if (!cleared && peg is LongPeg)
                    {
                        try
                        {
                            var hitField = HarmonyLib.AccessTools.Field(typeof(LongPeg), "_hit");
                            isLongPegHit = (bool)(hitField?.GetValue(peg) ?? false);
                        }
                        catch
                        {
                        }
                    }
                    // _cleared flag tracks "was this peg ever cleared this battle" — controls
                    // the previously-cleared background visual (dot/different color after refresh).
                    try
                    {
                        var clearedField = HarmonyLib.AccessTools.Field(typeof(Peg), "_cleared");
                        wasPreviouslyCleared = (bool)(clearedField?.GetValue(peg) ?? false);
                    }
                    catch
                    {
                    }
                }

                var entry = new PegEntry
                {
                    Guid = guid,
                    Index = i,
                    PegType = pt,
                    PegTypeName = peg.pegType.ToString(),
                    PosX = peg.transform.position.x,
                    PosY = peg.transform.position.y,
                    RotZ = peg.transform.localEulerAngles.z,
                    SlimeType = (int)peg.slimeType,
                    IsDestroyed = destroyed,
                    IsParentHidden = parentHidden,
                    IsCleared = cleared,
                    IsLongPegHit = isLongPegHit,
                    WasPreviouslyCleared = wasPreviouslyCleared,
                    CoinCount = peg.NumCoins(),
                    BuffAmount = peg.buffAmount,
                    IsBomb = isBombInstance,
                    HitCount = isBombInstance ? ((Bomb)peg).HitCount : 0,
                };
                CaptureShieldState(peg, entry);
                CaptureLpmParentPos(peg, entry);
                CaptureStructKey(peg, entry);
                snapshot.Pegs.Add(entry);

                if (peg.gameObject.activeInHierarchy && !destroyed)
                {
                    if (isBombInstance)
                    {
                        snapshot.BombPegCount++;
                    }
                    else if ((pt & 0x2) != 0)
                    {
                        snapshot.CritPegCount++;
                    }
                    else if ((pt & 0x8) != 0)
                    {
                        snapshot.ResetPegCount++;
                    }
                }
            }

            // Capture bombs from _bombs list (bombs are NOT in allPegs — they're separate)
            var bombsField = HarmonyLib.AccessTools.Field(typeof(Battle.PegManager), "_bombs");
            var bombs = bombsField?.GetValue(pm) as System.Collections.Generic.List<Bomb>;
            if (bombs != null)
            {
                for (var i = 0; i < bombs.Count; i++)
                {
                    var bomb = bombs[i];
                    if (bomb == null)
                    {
                        continue;
                    }

                    var guid = _pegId.GetOrAssignGuid(bomb);
                    var pt = (int)bomb.pegType;
                    var parentHidden = bomb.gameObject.activeSelf && !bomb.gameObject.activeInHierarchy;
                    var destroyed = !bomb.gameObject.activeInHierarchy || (pt & 0x20) != 0;
                    var cleared = false;
                    var wasPreviouslyCleared = false;
                    if (!destroyed)
                    {
                        try
                        {
                            cleared = bomb.IsDisabled();
                        }
                        catch
                        {
                        }

                        try
                        {
                            var clearedField = HarmonyLib.AccessTools.Field(typeof(Peg), "_cleared");
                            wasPreviouslyCleared = (bool)(clearedField?.GetValue(bomb) ?? false);
                        }
                        catch
                        {
                        }
                    }

                    var bombEntry = new PegEntry
                    {
                        Guid = guid,
                        Index = pegs.Count + i,
                        PegType = pt,
                        PegTypeName = bomb.pegType.ToString(),
                        PosX = bomb.transform.position.x,
                        PosY = bomb.transform.position.y,
                        RotZ = bomb.transform.localEulerAngles.z,
                        SlimeType = (int)bomb.slimeType,
                        IsDestroyed = destroyed,
                        IsParentHidden = parentHidden,
                        IsCleared = cleared,
                        WasPreviouslyCleared = wasPreviouslyCleared,
                        CoinCount = bomb.NumCoins(),
                        HitCount = bomb.HitCount,
                        IsBomb = true,
                        BuffAmount = bomb.buffAmount,
                    };
                    CaptureShieldState(bomb, bombEntry);
                    CaptureLpmParentPos(bomb, bombEntry);
                    CaptureStructKey(bomb, bombEntry);
                    snapshot.Pegs.Add(bombEntry);

                    if (bomb.gameObject.activeInHierarchy && !destroyed)
                    {
                        snapshot.BombPegCount++;
                    }

                    _log.LogInfo($"[PegProvider] BOMB[{i}] guid={guid} pos=({bomb.transform.position.x:F1},{bomb.transform.position.y:F1}) " +
                        $"type={bomb.pegType} active={bomb.gameObject.activeSelf} cleared={cleared} destroyed={destroyed} hits={bomb.HitCount}");
                }
            }

            // Capture bouncer pegs from _bouncerPegs (bouncers are NOT in allPegs — they're separate)
            var bouncers = pm.bouncerPegs;
            if (bouncers != null)
            {
                for (var i = 0; i < bouncers.Count; i++)
                {
                    var bouncer = bouncers[i];
                    if (bouncer == null)
                    {
                        continue;
                    }

                    var guid = _pegId.GetOrAssignGuid(bouncer);
                    var pt = (int)bouncer.pegType;
                    var parentHidden = bouncer.gameObject.activeSelf && !bouncer.gameObject.activeInHierarchy;
                    var destroyed = !bouncer.gameObject.activeInHierarchy || (pt & 0x20) != 0;
                    var cleared = false;
                    var wasPreviouslyCleared = false;
                    if (!destroyed)
                    {
                        try
                        {
                            cleared = bouncer.IsDisabled();
                        }
                        catch
                        {
                        }

                        try
                        {
                            var clearedField = HarmonyLib.AccessTools.Field(typeof(Peg), "_cleared");
                            wasPreviouslyCleared = (bool)(clearedField?.GetValue(bouncer) ?? false);
                        }
                        catch
                        {
                        }
                    }

                    var bouncerEntry = new PegEntry
                    {
                        Guid = guid,
                        Index = pegs.Count + (bombs?.Count ?? 0) + i,
                        PegType = pt,
                        PegTypeName = bouncer.pegType.ToString(),
                        PosX = bouncer.transform.position.x,
                        PosY = bouncer.transform.position.y,
                        RotZ = bouncer.transform.localEulerAngles.z,
                        SlimeType = (int)bouncer.slimeType,
                        IsDestroyed = destroyed,
                        IsParentHidden = parentHidden,
                        IsCleared = cleared,
                        WasPreviouslyCleared = wasPreviouslyCleared,
                        CoinCount = bouncer.NumCoins(),
                        IsBouncer = true,
                        BuffAmount = bouncer.buffAmount,
                    };
                    CaptureShieldState(bouncer, bouncerEntry);
                    CaptureLpmParentPos(bouncer, bouncerEntry);
                    CaptureStructKey(bouncer, bouncerEntry);
                    snapshot.Pegs.Add(bouncerEntry);

                    if (bouncer.gameObject.activeInHierarchy && !destroyed)
                    {
                        snapshot.BouncerPegCount++;
                    }
                }
            }

            snapshot.TotalPegCount = snapshot.Pegs.Count;

            // Capture bramball vines (pairs of peg GUIDs)
            try
            {
                var vinesField = HarmonyLib.AccessTools.Field(typeof(Battle.PegManager), "_vines");
                var vines = vinesField?.GetValue(pm) as System.Collections.Generic.List<Battle.Pachinko.Obstacles.PegBoardBramballVine>;
                if (vines != null && vines.Count > 0)
                {
                    var peg1Field = HarmonyLib.AccessTools.Field(typeof(Battle.Pachinko.Obstacles.PegBoardBramballVine), "_peg1");
                    var peg2Field = HarmonyLib.AccessTools.Field(typeof(Battle.Pachinko.Obstacles.PegBoardBramballVine), "_peg2");

                    foreach (var vine in vines)
                    {
                        if (vine == null)
                        {
                            continue;
                        }

                        var peg1 = peg1Field?.GetValue(vine) as Peg;
                        var peg2 = peg2Field?.GetValue(vine) as Peg;
                        if (peg1 == null || peg2 == null)
                        {
                            continue;
                        }

                        var guid1 = _pegId.GetGuid(peg1);
                        var guid2 = _pegId.GetGuid(peg2);
                        if (guid1 != null && guid2 != null)
                        {
                            snapshot.Vines.Add(new Snapshots.VineEntry
                            {
                                Peg1Guid = guid1,
                                Peg2Guid = guid2,
                            });
                        }
                    }

                    _log.LogInfo($"[PegProvider] Captured {snapshot.Vines.Count} bramball vines from {vines.Count} in _vines list");
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning($"[PegProvider] Failed to capture vines: {ex.Message}");
            }

            CaptureBlackHoles(snapshot);
            CaptureSplineGenerators(snapshot);

            var bombsListCount = bombs?.Count ?? -1;
            // Composition signature — log only when any count changes vs the
            // previous capture, otherwise this fires every heartbeat with the
            // exact same numbers (~7000 lines per session).
            var sig = (snapshot.TotalPegCount, snapshot.CritPegCount, snapshot.BombPegCount,
                snapshot.ResetPegCount, snapshot.BouncerPegCount, _pegId.Count, bombsListCount,
                allPegsBombCount, snapshot.BlackHoles.Count, snapshot.SplineGenerators.Count);
            if (!sig.Equals(_lastCaptureSig))
            {
                _lastCaptureSig = sig;
                _log.LogInfo($"[PegProvider] Captured {snapshot.TotalPegCount} pegs from PegManager " +
                    $"(crit={snapshot.CritPegCount}, bomb={snapshot.BombPegCount}, reset={snapshot.ResetPegCount}, " +
                    $"bouncer={snapshot.BouncerPegCount}, registry={_pegId.Count}, " +
                    $"_bombs={bombsListCount}, allPegsBombs={allPegsBombCount}, " +
                    $"blackHoles={snapshot.BlackHoles.Count}, splineGens={snapshot.SplineGenerators.Count})");
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"PegboardStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }

    private static void CaptureShieldState(Peg peg, PegEntry entry)
    {
        try
        {
            if (!peg.shielded)
            {
                return;
            }

            entry.IsShielded = true;
            var overlayField = HarmonyLib.AccessTools.Field(typeof(Peg), "PegShieldOverlayInstance");
            var overlay = overlayField?.GetValue(peg) as Battle.PegBehaviour.PegShieldOverlay;
            if (overlay != null)
            {
                entry.ShieldHitCount = overlay.hitCount;
                entry.ShieldHitLimit = overlay.hitLimit;
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// If the peg is under a LinearPegMovement parent (self or direct parent),
    /// capture the parent's world position for direct sync on the client.
    /// </summary>
    private static void CaptureLpmParentPos(Peg peg, PegEntry entry)
    {
        try
        {
            var lpm = peg.GetComponent<LinearPegMovement>();
            if (lpm == null && peg.transform.parent != null)
            {
                lpm = peg.transform.parent.GetComponent<LinearPegMovement>();
            }
            // Also check grandparent — bombs are children of the original peg,
            // which is a child of the LPM row.
            if (lpm == null && peg.transform.parent?.parent != null)
            {
                lpm = peg.transform.parent.parent.GetComponent<LinearPegMovement>();
            }

            if (lpm != null)
            {
                entry.LpmParentPosX = lpm.transform.position.x;
                entry.LpmParentPosY = lpm.transform.position.y;
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Capture a structural key (parent-name + local-position) that is stable
    /// across host/client because it's baked into the prefab hierarchy.
    ///
    /// Needed because pm.allPegs ordering is NOT deterministic between host
    /// and client (especially MinotaurLayout) — relying on the list index to
    /// bind GUIDs corrupts bindings permanently. A structural key lets the
    /// client find the matching client peg regardless of allPegs ordering.
    /// </summary>
    private static void CaptureStructKey(Peg peg, PegEntry entry)
    {
        try
        {
            var parent = peg.transform.parent;
            // Use the full ancestor chain (joined by '/') instead of just parent.name.
            // Spirit of Radia's PegLayoutAlternator has two sibling pegboards (pegboardA
            // and pegboardB) whose internal hierarchies are identical — both contain a
            // child literally named "PegSplineFollowGenerator" with the same sibling
            // count. Keying only on parent.name made the (parent.name, siblingIndex)
            // struct index collide between the two pegboards: a peg from the active
            // pegboardA could bind to a peg in the inactive pegboardB (or vice versa).
            // After misbinding, EnsureParentChainActive reactivates the wrong pegboard
            // and IsParentHidden hides the wrong pegs — visible as "some pegs that
            // should be hidden are showing, some that should show are hidden".
            entry.ParentName = ParentChainKey(parent);
            var lp = peg.transform.localPosition;
            entry.LocalPosX = lp.x;
            entry.LocalPosY = lp.y;
            entry.SiblingIndex = peg.transform.GetSiblingIndex();

            // HasLpm: name is historical — true whenever the peg's transform is
            // continuously driven by *any* movement component on self/ancestors.
            // Covers LPM (whole-row drift), PegSplineFollow / RotatingPegCircle
            // (parent generators that overwrite each child's transform.position
            // every FixedUpdate), and the IDummiableMovingPeg family (per-peg
            // movers like PegMoveAndReturn, PegSquareMovement, PegTeleport,
            // SimpleMovablePeg, FireworkMovement). For all of these the peg's
            // localPosition drifts mid-frame, so the applier must fall back to
            // (ParentName, SiblingIndex) matching instead of (ParentName,
            // LocalPos). Without this, e.g. Spirit of Radia's spline-following
            // pegs ("figure 8" loops) bind to the wrong client pegs and only
            // half of the loop animates correctly.
            entry.HasLpm = HasMovingAncestor(peg.transform);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Enumerate all live <see cref="Battle.Pachinko.Obstacles.PegboardBlackHole"/>
    /// instances in the scene and ship their world positions. The client blocks
    /// the boss action that spawns them, so without this they're invisible.
    /// FindObjectsOfType skips inactive — black holes are toggled active/inactive
    /// during shot pauses, so include inactive ones for the visual mirror.
    /// </summary>
    private void CaptureBlackHoles(PegboardStateSnapshot snapshot)
    {
        try
        {
            var holes = UnityEngine.Resources.FindObjectsOfTypeAll<Battle.Pachinko.Obstacles.PegboardBlackHole>();
            var idx = 0;
            for (var i = 0; i < holes.Length; i++)
            {
                var h = holes[i];
                if (h == null || h.gameObject == null)
                {
                    continue;
                }

                if (h.gameObject.scene.name == null)
                {
                    continue;
                }

                var pos = h.transform.position;
                snapshot.BlackHoles.Add(new Snapshots.BlackHoleEntry
                {
                    Index = idx,
                    PosX = pos.x,
                    PosY = pos.y,
                });
                idx++;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[PegProvider] Failed to capture black holes: {ex.Message}");
        }
    }

    /// <summary>
    /// Capture each <see cref="PegSplineFollow"/> generator's current spline
    /// phase. The client runs PegSplineFollow.FixedUpdate independently and
    /// drifts; the applier resets <c>_pegSplineDistances</c> from this each
    /// heartbeat so positions converge instead of slowly walking apart.
    /// </summary>
    private void CaptureSplineGenerators(PegboardStateSnapshot snapshot)
    {
        try
        {
            var distancesField = HarmonyLib.AccessTools.Field(typeof(PegSplineFollow), "_pegSplineDistances");
            if (distancesField == null)
            {
                return;
            }

            var generators = UnityEngine.Object.FindObjectsOfType<PegSplineFollow>();
            for (var i = 0; i < generators.Length; i++)
            {
                var g = generators[i];
                if (g == null)
                {
                    continue;
                }

                var distances = distancesField.GetValue(g) as System.Collections.Generic.List<float>;
                if (distances == null || distances.Count == 0)
                {
                    continue;
                }

                var parent = g.transform.parent;
                var entry = new Snapshots.SplineGeneratorEntry
                {
                    HierarchyPath = HierarchyPath(g.transform),
                    Phase = distances[0],
                    NumPegs = distances.Count,
                };
                // Snapshot the full per-peg distance list so the client can write
                // distances element-by-element (no "shift by phase" heuristic).
                // Cheap on the wire — typical generator has 10-30 pegs.
                entry.Distances.Capacity = distances.Count;
                for (var d = 0; d < distances.Count; d++)
                {
                    entry.Distances.Add(distances[d]);
                }

                if (parent != null)
                {
                    var pp = parent.position;
                    entry.ParentPosX = pp.x;
                    entry.ParentPosY = pp.y;
                    entry.HasParent = true;
                }

                snapshot.SplineGenerators.Add(entry);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[PegProvider] Failed to capture spline generators: {ex.Message}");
        }
    }

    /// <summary>
    /// Build a slash-joined ancestor chain for a peg's parent transform. Used as the
    /// struct-key disambiguator so sibling pegboards with identical internal layouts
    /// (e.g. PegLayoutAlternator's pegboardA / pegboardB) don't share keys.
    /// </summary>
    public static string ParentChainKey(UnityEngine.Transform parent)
    {
        if (parent == null)
        {
            return string.Empty;
        }

        return HierarchyPath(parent);
    }

    // Caches keyed on Transform InstanceID. Both the parent ancestor chain
    // and the "has moving ancestor" predicate are stable for the lifetime of
    // a transform (peg parents never re-parent, movement components never get
    // added/removed at runtime). InstanceID is unique per object lifetime, so
    // a destroyed-and-replaced parent gets a different ID — no stale reads.
    // Cleared on battle init via ClearHierarchyCaches.
    private static readonly System.Collections.Generic.Dictionary<int, string> _hierarchyPathCache
        = new System.Collections.Generic.Dictionary<int, string>(256);

    private static readonly System.Collections.Generic.Dictionary<int, bool> _movingAncestorCache
        = new System.Collections.Generic.Dictionary<int, bool>(256);

    [System.ThreadStatic]
    private static System.Collections.Generic.List<string> _hierarchyPathBuf;

    public static void ClearHierarchyCaches()
    {
        _hierarchyPathCache.Clear();
        _movingAncestorCache.Clear();
    }

    private static string HierarchyPath(UnityEngine.Transform t)
    {
        if (t == null)
        {
            return string.Empty;
        }

        var id = t.GetInstanceID();
        if (_hierarchyPathCache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        // Forward-build then reverse-join: avoids StringBuilder.Insert(0,...)
        // which copies the whole buffer per ancestor (O(D^2)).
        var buf = _hierarchyPathBuf ??= new System.Collections.Generic.List<string>(8);
        buf.Clear();
        for (var cur = t; cur != null; cur = cur.parent)
        {
            buf.Add(cur.name);
        }

        buf.Reverse();
        var path = string.Join("/", buf);
        _hierarchyPathCache[id] = path;
        return path;
    }

    private static bool HasMovingAncestor(UnityEngine.Transform t)
    {
        if (t == null)
        {
            return false;
        }

        var id = t.GetInstanceID();
        if (_movingAncestorCache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var result = false;
        for (var cur = t; cur != null; cur = cur.parent)
        {
            if (cur.GetComponent<LinearPegMovement>() != null
                || cur.GetComponent<PegSplineFollow>() != null
                || cur.GetComponent<RotatingPegCircle>() != null
                || cur.GetComponent<PegMoveAndReturn>() != null
                || cur.GetComponent<PegSquareMovement>() != null
                || cur.GetComponent<PegTeleport>() != null
                || cur.GetComponent<SimpleMovablePeg>() != null
                || cur.GetComponent<FireworkMovement>() != null)
            {
                result = true;
                break;
            }
        }

        _movingAncestorCache[id] = result;
        return result;
    }
}
