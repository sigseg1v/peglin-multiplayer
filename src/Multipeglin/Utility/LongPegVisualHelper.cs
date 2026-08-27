using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Multipeglin.Utility;

/// <summary>
/// Helpers for replicating LongPeg's host-side hit visual on the client,
/// plus refresh-safe pop/heal that never permanently Destroy()s the main collider.
///
/// Native HidePeg does Object.Destroy(_collider), after which HardReset /
/// SetActiveStatus cannot resurrect the peg — hence the soft-hide path here.
/// </summary>
public static class LongPegVisualHelper
{
    // Resolved once. AccessTools.Field/Method are uncached — each call does a
    // GetField/GetMethod walk up the type's base chain — and these run per peg
    // per apply on a board of ~90 LongPegs.
    private static readonly System.Reflection.FieldInfo HitField
        = AccessTools.Field(typeof(LongPeg), "_hit");

    private static readonly System.Reflection.FieldInfo ClearedField
        = AccessTools.Field(typeof(global::Peg), "_cleared");

    private static readonly System.Reflection.FieldInfo RendererField
        = AccessTools.Field(typeof(LongPeg), "_renderer");

    private static readonly System.Reflection.FieldInfo ColorsField
        = AccessTools.Field(typeof(LongPeg), "_colors");

    private static readonly System.Reflection.FieldInfo ActiveMatField
        = AccessTools.Field(typeof(LongPeg), "_activeMaterial");

    private static readonly System.Reflection.FieldInfo DestroyedMatField
        = AccessTools.Field(typeof(LongPeg), "_destroyedMaterial");

    private static readonly System.Reflection.FieldInfo PoppedTriggerField
        = AccessTools.Field(typeof(global::Peg), "_poppedPegTrigger");

    private static readonly System.Reflection.FieldInfo ResetOrCritSpriteField
        = AccessTools.Field(typeof(LongPeg), "_resetOrCritSprite");

    private static readonly System.Reflection.FieldInfo PegTextField
        = AccessTools.Field(typeof(LongPeg), "_pegText");

    private static readonly System.Reflection.FieldInfo BeingHitField
        = AccessTools.Field(typeof(LongPeg), "_beingHit");

    private static readonly System.Reflection.FieldInfo BeingHitByOrbField
        = AccessTools.Field(typeof(LongPeg), "_beingHitByOrb");

    private static readonly System.Reflection.FieldInfo TimeHitField
        = AccessTools.Field(typeof(LongPeg), "_timeHit");

    private static readonly System.Reflection.FieldInfo ColliderField
        = AccessTools.Field(typeof(global::Peg), "_collider");

    private static readonly System.Reflection.FieldInfo TriggerField
        = AccessTools.Field(typeof(global::Peg), "_trigger");

    private static readonly System.Reflection.FieldInfo PoppedColliderField
        = AccessTools.Field(typeof(global::Peg), "_poppedPegCollider");

    private static readonly System.Reflection.FieldInfo SpecialColliderField
        = AccessTools.Field(typeof(global::Peg), "_specialPegCollider");

    private static readonly System.Reflection.MethodBase InitializeComponentsMethod
        = AccessTools.Method(typeof(LongPeg), "InitializeComponents");

    /// <summary>Cached "Hit" field of the LongPegColors struct; resolved on first use.</summary>
    private static System.Reflection.FieldInfo _hitColorField;

    private static bool _hitColorFieldResolved;

    public static void ApplyHitVisual(LongPeg peg)
    {
        if (peg == null)
        {
            return;
        }

        try
        {
            HitField?.SetValue(peg, true);
            ClearedField?.SetValue(peg, true);

            var renderer = RendererField?.GetValue(peg) as MeshRenderer;
            if (renderer == null)
            {
                return;
            }

            var poppedTrigger = PoppedTriggerField?.GetValue(peg) as Collider2D;
            var useDestroyed = poppedTrigger != null && poppedTrigger.enabled;

            var matField = useDestroyed ? DestroyedMatField : ActiveMatField;
            var mat = matField?.GetValue(peg) as Material;
            if (mat != null)
            {
                renderer.material = mat;
            }

            var colors = ColorsField?.GetValue(peg);
            if (colors != null)
            {
                if (!_hitColorFieldResolved)
                {
                    _hitColorField = colors.GetType().GetField("Hit");
                    _hitColorFieldResolved = true;
                }

                if (_hitColorField != null && renderer.material != null)
                {
                    var hitColor = (Color)_hitColorField.GetValue(colors);
                    renderer.material.color = hitColor;
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Host says this LongPeg is alive (collider on). Kill fade tweens, clear
    /// delayed-death flags, re-bind a missing _collider if needed, HardReset,
    /// and assert !IsDisabled().
    /// </summary>
    public static bool ForceAlive(LongPeg peg, ManualLogSource log = null)
    {
        if (peg == null)
        {
            return false;
        }

        KillFadeTweens(peg);
        ClearBeingHit(peg);

        if (peg.pegType == Peg.PegType.DESTROYED)
        {
            peg.pegType = Peg.PegType.REGULAR;
        }

        EnsureColliderBound(peg, log);

        try
        {
            peg.HardReset();
            peg.SetActiveStatus(active: true);
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[LongPegHeal] HardReset threw: {ex.Message}");
        }

        EnsureColliderBound(peg, log);
        ForceCollidersAlive(peg);
        peg.gameObject.SetActive(true);

        var ok = false;
        try
        {
            ok = !peg.IsDisabled();
        }
        catch
        {
            ok = false;
        }

        if (!ok)
        {
            log?.LogWarning("[LongPegHeal] ForceAlive failed — still IsDisabled()");
        }

        return ok;
    }

    /// <summary>
    /// Host says popped. Collider off + gray/destroyed look.
    /// Does NOT start RemoveIfCleared's DOFade→SetActive(false) (refresh footgun).
    /// Leaves GameObject active unless the caller deactivates it (IsDestroyed path).
    /// </summary>
    public static void ForcePopped(LongPeg peg)
    {
        if (peg == null)
        {
            return;
        }

        KillFadeTweens(peg);
        ClearBeingHit(peg);
        ApplyHitVisual(peg);

        try
        {
            peg.SetActiveStatus(active: false);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Mid-battle / snapshot "destroyed" for LongPeg: match host inactive without
    /// calling DestroyPeg/HidePeg (which Object.Destroy the main collider).
    /// </summary>
    public static void SoftHide(LongPeg peg)
    {
        if (peg == null)
        {
            return;
        }

        ForcePopped(peg);
        peg.gameObject.SetActive(false);
    }

    public static void KillFadeTweens(LongPeg peg)
    {
        if (peg == null)
        {
            return;
        }

        try
        {
            DG.Tweening.DOTween.Kill(peg.gameObject, complete: false);
            foreach (var t in peg.GetComponentsInChildren<Transform>(true))
            {
                DG.Tweening.DOTween.Kill(t, complete: false);
            }

            foreach (var r in peg.GetComponentsInChildren<Renderer>(true))
            {
                DG.Tweening.DOTween.Kill(r, complete: false);
                if (r.material != null)
                {
                    DG.Tweening.DOTween.Kill(r.material, complete: false);
                }
            }

            var overlay = ResetOrCritSpriteField?.GetValue(peg) as SpriteRenderer;
            if (overlay != null)
            {
                DG.Tweening.DOTween.Kill(overlay, complete: false);
            }

            var pegText = PegTextField?.GetValue(peg);
            if (pegText != null)
            {
                DG.Tweening.DOTween.Kill(pegText, complete: false);
            }
        }
        catch
        {
        }
    }

    private static void ClearBeingHit(LongPeg peg)
    {
        try
        {
            BeingHitField?.SetValue(peg, false);
            BeingHitByOrbField?.SetValue(peg, null);
            TimeHitField?.SetValue(peg, 0f);
        }
        catch
        {
        }
    }

    private static void EnsureColliderBound(LongPeg peg, ManualLogSource log)
    {
        var field = ColliderField;
        if (field == null)
        {
            log?.LogWarning("[LongPegHeal] EnsureColliderBound: Peg._collider field missing");
            return;
        }

        Collider2D col = null;
        try
        {
            col = field.GetValue(peg) as Collider2D;
        }
        catch
        {
        }

        // Unity fake-null: destroyed components compare equal to null.
        if (col != null)
        {
            return;
        }

        // LongPeg.InitializeComponents (private) does `_collider = GetComponent<Collider2D>()`.
        // Peg itself declares no such method, so do not try a base-type variant —
        // AccessTools would just return null and the call would silently no-op.
        try
        {
            InitializeComponentsMethod?.Invoke(peg, null);
            col = field.GetValue(peg) as Collider2D;
            if (col != null)
            {
                return;
            }
        }
        catch
        {
        }

        try
        {
            foreach (var c in peg.GetComponents<Collider2D>())
            {
                if (c != null && !c.isTrigger)
                {
                    field.SetValue(peg, c);
                    return;
                }
            }

            var any = peg.GetComponentInChildren<Collider2D>(true);
            if (any != null)
            {
                field.SetValue(peg, any);
                return;
            }
        }
        catch
        {
        }

        log?.LogWarning("[LongPegHeal] EnsureColliderBound failed — no Collider2D on peg");
    }

    private static void ForceCollidersAlive(LongPeg peg)
    {
        void SetEnabled(System.Reflection.FieldInfo colliderField, bool enabled)
        {
            try
            {
                var c = colliderField?.GetValue(peg) as Collider2D;
                if (c != null)
                {
                    c.enabled = enabled;
                }
            }
            catch
            {
            }
        }

        // Mirror SetActiveStatus(true) collider matrix. All five are declared on
        // Peg itself (Peg.cs:152,155,158,161,164), not on the subclasses.
        SetEnabled(ColliderField, true);
        SetEnabled(TriggerField, true);
        SetEnabled(PoppedTriggerField, false);
        SetEnabled(PoppedColliderField, false);
        SetEnabled(SpecialColliderField, false);
    }

    /// <summary>
    /// World-space centre of a LongPeg's geometry, computed from the live
    /// transform + mesh bounds.
    ///
    /// Why not just <c>Peg.GetCenterOfPeg()</c>: that method only recomputes
    /// from collider bounds when a collider <c>isActiveAndEnabled</c>. At
    /// battle start (and any time the peg is hidden/popped) every collider is
    /// off, so it returns the stale cached <c>_position</c> instead. On the
    /// host that cache is poisoned by the pre-instancing path — pegboards are
    /// built at <c>PegLayoutLoader.PRE_INSTANCED_OFFSET</c> (+1000, 0, 0) and
    /// <c>InitPegText()</c>/<c>InitCoinPrefab()</c> run *while the board is
    /// still parked there*, so <c>_position</c> is cached +1000 in X and the
    /// board is only moved back afterwards. Snapshots captured at battle start
    /// then shipped long-peg centres 1000 units off, nothing matched, and the
    /// applier cloned 39 pegs on top of each other while deactivating the real
    /// ones.
    ///
    /// The mesh path is immune to both problems: <c>mesh.bounds</c> is local
    /// space (the generator writes the quad's corners as local vertices in
    /// <c>BezierMeshGenerator.GenerateMesh</c>) and <c>TransformPoint</c> reads
    /// the transform as it is *right now*.
    /// </summary>
    public static Vector3 WorldCenter(global::Peg peg)
    {
        if (peg == null)
        {
            return Vector3.zero;
        }

        try
        {
            var filter = peg.GetComponent<MeshFilter>();
            if (filter != null)
            {
                var mesh = filter.sharedMesh;
                if (mesh != null && mesh.vertexCount > 0)
                {
                    return peg.transform.TransformPoint(mesh.bounds.center);
                }
            }
        }
        catch
        {
        }

        try
        {
            // Collider2D.bounds is already world space, but only meaningful
            // while the collider is enabled — same limitation as GetCenterOfPeg.
            var col = peg.GetComponent<Collider2D>();
            if (col != null && col.isActiveAndEnabled)
            {
                return col.bounds.center;
            }
        }
        catch
        {
        }

        try
        {
            return peg.GetCenterOfPeg();
        }
        catch
        {
            return peg.transform.position;
        }
    }
}
