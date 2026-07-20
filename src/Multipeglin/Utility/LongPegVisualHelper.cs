using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Multipeglin.Utility;

/// <summary>
/// Helpers for replicating LongPeg's host-side hit visual on the client,
/// plus refresh-safe pop/heal that never permanently Destroy()s the main collider.
///
/// See wiki/plans/longpeg-heal-failure.md (RC6): native HidePeg destroys _collider,
/// after which HardReset/SetActiveStatus cannot resurrect the peg.
/// </summary>
public static class LongPegVisualHelper
{
    public static void ApplyHitVisual(LongPeg peg)
    {
        if (peg == null)
        {
            return;
        }

        try
        {
            var hitField = AccessTools.Field(typeof(LongPeg), "_hit");
            var clearedField = AccessTools.Field(typeof(global::Peg), "_cleared");
            var rendererField = AccessTools.Field(typeof(LongPeg), "_renderer");
            var colorsField = AccessTools.Field(typeof(LongPeg), "_colors");
            var activeMatField = AccessTools.Field(typeof(LongPeg), "_activeMaterial");
            var destroyedMatField = AccessTools.Field(typeof(LongPeg), "_destroyedMaterial");
            var poppedTriggerField = AccessTools.Field(typeof(global::Peg), "_poppedPegTrigger");

            hitField?.SetValue(peg, true);
            clearedField?.SetValue(peg, true);

            var renderer = rendererField?.GetValue(peg) as MeshRenderer;
            if (renderer == null)
            {
                return;
            }

            var poppedTrigger = poppedTriggerField?.GetValue(peg) as Collider2D;
            var useDestroyed = poppedTrigger != null && poppedTrigger.enabled;

            var matField = useDestroyed ? destroyedMatField : activeMatField;
            var mat = matField?.GetValue(peg) as Material;
            if (mat != null)
            {
                renderer.material = mat;
            }

            var colors = colorsField?.GetValue(peg);
            if (colors != null)
            {
                var hitColorField = colors.GetType().GetField("Hit");
                if (hitColorField != null && renderer.material != null)
                {
                    var hitColor = (Color)hitColorField.GetValue(colors);
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

            var overlay = AccessTools.Field(typeof(LongPeg), "_resetOrCritSprite")?.GetValue(peg) as SpriteRenderer;
            if (overlay != null)
            {
                DG.Tweening.DOTween.Kill(overlay, complete: false);
            }

            var pegText = AccessTools.Field(typeof(LongPeg), "_pegText")?.GetValue(peg);
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
            AccessTools.Field(typeof(LongPeg), "_beingHit")?.SetValue(peg, false);
            AccessTools.Field(typeof(LongPeg), "_beingHitByOrb")?.SetValue(peg, null);
            AccessTools.Field(typeof(LongPeg), "_timeHit")?.SetValue(peg, 0f);
        }
        catch
        {
        }
    }

    private static void EnsureColliderBound(LongPeg peg, ManualLogSource log)
    {
        var field = AccessTools.Field(typeof(Peg), "_collider");
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

        try
        {
            AccessTools.Method(typeof(LongPeg), "InitializeComponents")?.Invoke(peg, null);
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
            AccessTools.Method(typeof(Peg), "InitializeComponents")?.Invoke(peg, null);
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
        void SetEnabled(string fieldName, Type declaringType, bool enabled)
        {
            try
            {
                var c = AccessTools.Field(declaringType, fieldName)?.GetValue(peg) as Collider2D;
                if (c != null)
                {
                    c.enabled = enabled;
                }
            }
            catch
            {
            }
        }

        // Mirror SetActiveStatus(true) collider matrix (Peg base fields).
        SetEnabled("_collider", typeof(Peg), true);
        SetEnabled("_trigger", typeof(Peg), true);
        SetEnabled("_poppedPegTrigger", typeof(Peg), false);
        SetEnabled("_poppedPegCollider", typeof(Peg), false);
        SetEnabled("_specialPegCollider", typeof(Peg), false);
    }
}
