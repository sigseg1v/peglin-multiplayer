using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Multipeglin.Utility;

/// <summary>
/// Client-side bomb fuse / detonation visuals without running native
/// <see cref="Bomb.PegActivated"/> (relic splash, nav gold, etc.).
///
/// Native lifecycle (Peglin 2.0.12):
///   HitCount 0 → untouched material
///   HitCount 1 → explode material + NumHits animator
///   HitCount &gt; 1 → _detonated, collider off, then SetActive(false)
/// </summary>
public static class BombVisualHelper
{
    // Resolved once — AccessTools.Field does an uncached GetField walk per call,
    // and ForceState runs per bomb per apply.
    private static readonly System.Reflection.FieldInfo AnimatorField
        = AccessTools.Field(typeof(Bomb), "_animator");

    private static readonly System.Reflection.FieldInfo AnimHitsKeyField
        = AccessTools.Field(typeof(Bomb), "_animHitsKey");

    private static readonly System.Reflection.FieldInfo ColliderField
        = AccessTools.Field(typeof(Peg), "_collider");

    private static readonly System.Reflection.FieldInfo UntouchedMaterialField
        = AccessTools.Field(typeof(Bomb), "_untouchedMaterial");

    private static readonly System.Reflection.FieldInfo ExplodeMaterialField
        = AccessTools.Field(typeof(Bomb), "_explodeMaterial");

    private static readonly System.Reflection.FieldInfo DetonatedField
        = AccessTools.Field(typeof(Bomb), "_detonated");

    private static readonly System.Reflection.FieldInfo DetonatedThisTurnField
        = AccessTools.Field(typeof(Bomb), "_detonatedThisTurn");

    /// <summary>
    /// Bombs we have just forced into the detonated state, keyed by GameObject
    /// instance id, mapped to the Time.time after which we stop waiting for the
    /// native explosion clip and hard-hide instead.
    ///
    /// Native Bomb.LateUpdate deactivates the GameObject the frame the
    /// animator reaches "SetInvis", i.e. after the explode clip has played. Hiding the
    /// GameObject ourselves the moment the host reports HitCount &gt; 1 skips that clip
    /// and the bomb just vanishes on the client. So we leave it visible and let the
    /// game do the hiding — with this deadline as the backstop in case the controller
    /// never reaches SetInvis (no animator, forced state jump, prefab variant).
    /// </summary>
    private static readonly Dictionary<int, float> DetonationGrace = new Dictionary<int, float>();

    private const float DetonationGraceSeconds = 1.5f;

    /// <summary>
    /// True when the client bomb already matches the host's hit count and the
    /// derived visual state, so ForceState would be a no-op. Lets the applier skip
    /// the per-apply rewrite for every fused bomb on the board.
    /// </summary>
    public static bool MatchesState(Bomb bomb, int hitCount)
    {
        if (bomb == null)
        {
            return false;
        }

        if (bomb.HitCount != hitCount)
        {
            return false;
        }

        try
        {
            var detonated = (bool)(DetonatedField?.GetValue(bomb) ?? false);
            var active = bomb.gameObject.activeSelf;

            if (hitCount > 1)
            {
                return detonated && !active;
            }

            if (detonated || !active)
            {
                return false;
            }

            // Fuse material is the whole point of the HitCount==1 state (native
            // Bomb sets _collider.sharedMaterial = _explodeMaterial at 1 hit and
            // restores _untouchedMaterial on reset). A bomb can carry the right
            // HitCount with a stale material after a refresh/heal, so check it —
            // otherwise this returns "in sync" and the caller skips the repair.
            var collider = ColliderField?.GetValue(bomb) as Collider2D;
            if (collider == null)
            {
                return false;
            }

            var expected = hitCount == 1
                ? ExplodeMaterialField?.GetValue(bomb) as PhysicsMaterial2D
                : UntouchedMaterialField?.GetValue(bomb) as PhysicsMaterial2D;

            // Null expected material = the prefab never had one; nothing to compare.
            return expected == null || collider.sharedMaterial == expected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Force client bomb to match host hit-count state.
    /// When <paramref name="hideIfDetonated"/> is true (default), HitCount &gt; 1
    /// immediately deactivates the GO so we don't wait for IsDestroyed heartbeat.
    /// </summary>
    public static void ForceState(
        Bomb bomb,
        int hitCount,
        ManualLogSource log = null,
        bool hideIfDetonated = true)
    {
        if (bomb == null)
        {
            return;
        }

        if (hitCount < 0)
        {
            hitCount = 0;
        }

        try
        {
            var previous = bomb.HitCount;
            bomb.HitCount = hitCount;

            // `!animator` uses UnityEngine.Object's implicit bool (fake-null aware);
            // `??=` would only test reference null and keep a destroyed Animator.
            var animator = bomb.GetComponent<Animator>();
            if (!animator)
            {
                animator = AnimatorField?.GetValue(bomb) as Animator;
            }

            var animKey = AnimHitsKeyField?.GetValue(bomb) as string ?? "NumHits";

            // Walking the fuse BACKWARDS is a state the native game never performs:
            // Bomb only ever raises HitCount, and Reset() runs solely on detonated
            // bombs. The controller therefore has no transition out of the lit-fuse
            // state on NumHits dropping, so SetInteger alone leaves the parameter
            // correct and the bomb still drawn with a burning fuse — exactly what a
            // client holding Short Fuse showed (its local Bomb.Start /
            // HandleRelicAddition lights every bomb the host has not hit).
            // Rebind() returns the controller to its default state; Update(0) applies
            // it in the same frame instead of one frame later.
            var steppingDown = animator
                && (hitCount < previous || hitCount < animator.GetInteger(animKey))
                && bomb.gameObject.activeInHierarchy;

            if (steppingDown)
            {
                animator.Rebind();
            }

            // Native Bomb only ever raises HitCount one at a time (PegActivated
            // increments, then sets the parameter), so the controller's only route
            // into the explode clip is out of the lit-fuse state. A heartbeat can hand
            // us 0 → 2 in a single apply; walk through 1 first so the transition the
            // controller actually has is the one we ask it to take.
            if (animator
                && hitCount > 1
                && animator.GetInteger(animKey) < 1
                && bomb.gameObject.activeInHierarchy)
            {
                animator.SetInteger(animKey, 1);
                animator.Update(0f);
            }

            animator?.SetInteger(animKey, hitCount);

            if (steppingDown)
            {
                animator.Update(0f);
            }

            var collider = ColliderField?.GetValue(bomb) as Collider2D;
            var untouched = UntouchedMaterialField?.GetValue(bomb) as PhysicsMaterial2D;
            var explode = ExplodeMaterialField?.GetValue(bomb) as PhysicsMaterial2D;
            var detonatedField = DetonatedField;
            var detonatedThisTurnField = DetonatedThisTurnField;
            var instanceId = bomb.gameObject.GetInstanceID();

            if (hitCount <= 0)
            {
                DetonationGrace.Remove(instanceId);
                detonatedField?.SetValue(bomb, false);
                detonatedThisTurnField?.SetValue(bomb, false);
                if (collider != null)
                {
                    collider.enabled = true;
                    if (untouched != null)
                    {
                        collider.sharedMaterial = untouched;
                    }
                }

                if (!bomb.gameObject.activeSelf)
                {
                    bomb.gameObject.SetActive(true);
                }
            }
            else if (hitCount == 1)
            {
                DetonationGrace.Remove(instanceId);
                detonatedField?.SetValue(bomb, false);
                if (collider != null)
                {
                    collider.enabled = true;
                    if (explode != null)
                    {
                        collider.sharedMaterial = explode;
                    }
                }

                if (!bomb.gameObject.activeSelf)
                {
                    bomb.gameObject.SetActive(true);
                }
            }
            else
            {
                // Detonated: match host spent state without relic splash.
                detonatedField?.SetValue(bomb, true);
                detonatedThisTurnField?.SetValue(bomb, true);
                if (collider != null)
                {
                    collider.enabled = false;
                }

                if (!hideIfDetonated || !bomb.gameObject.activeSelf)
                {
                    // Already hidden (native LateUpdate got there), or the caller is
                    // reviving the bomb and will re-activate it.
                    DetonationGrace.Remove(instanceId);
                }
                else if (!animator || !bomb.gameObject.activeInHierarchy)
                {
                    // Nothing is going to play a clip — hide immediately as before.
                    bomb.gameObject.SetActive(false);
                    DetonationGrace.Remove(instanceId);
                }
                else if (!DetonationGrace.TryGetValue(instanceId, out var deadline))
                {
                    // First apply that detonates this bomb: leave it on screen so the
                    // explode → SetInvis clip runs. Bomb.LateUpdate deactivates it as
                    // soon as the animator lands on SetInvis.
                    SweepDetonationGrace();
                    DetonationGrace[instanceId] = Time.time + DetonationGraceSeconds;
                }
                else if (Time.time >= deadline)
                {
                    // Controller never reached SetInvis; stop waiting.
                    bomb.gameObject.SetActive(false);
                    DetonationGrace.Remove(instanceId);
                }
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[BombSync] ForceState threw: {ex.Message}");
        }
    }

    /// <summary>
    /// Host says bomb is gone (IsDestroyed). Hide without DestroyPeg churn.
    /// </summary>
    public static void SoftHide(Bomb bomb, ManualLogSource log = null)
    {
        if (bomb == null)
        {
            return;
        }

        var hits = bomb.HitCount < 2 ? 2 : bomb.HitCount;
        ForceState(bomb, hits, log, hideIfDetonated: true);

        // ForceState deliberately leaves a freshly-detonated bomb visible so the
        // native explosion clip can play; don't undo that here. The next apply
        // (or Bomb.LateUpdate, whichever comes first) hides it.
        if (bomb.gameObject.activeSelf
            && !DetonationGrace.ContainsKey(bomb.gameObject.GetInstanceID()))
        {
            bomb.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Drop grace entries for bombs that were destroyed mid-explosion so the map
    /// can't grow across a long run. Only walks the map once it is big enough to
    /// be worth walking — a pegboard has far fewer bombs than this.
    /// </summary>
    private static void SweepDetonationGrace()
    {
        if (DetonationGrace.Count < 64)
        {
            return;
        }

        var cutoff = Time.time - 30f;
        List<int> stale = null;
        foreach (var kvp in DetonationGrace)
        {
            if (kvp.Value < cutoff)
            {
                (stale ??= new List<int>()).Add(kvp.Key);
            }
        }

        if (stale == null)
        {
            return;
        }

        foreach (var key in stale)
        {
            DetonationGrace.Remove(key);
        }
    }

    /// <summary>
    /// Host revived / reset a bomb (alive, HitCount 0 or 1). Re-enable and apply state.
    /// Does not depend on native CanResetBomb().
    /// </summary>
    public static void ForceAlive(Bomb bomb, int hitCount, ManualLogSource log = null)
    {
        if (bomb == null)
        {
            return;
        }

        if (hitCount > 1)
        {
            hitCount = 0;
        }

        ForceState(bomb, hitCount, log, hideIfDetonated: false);
        if (!bomb.gameObject.activeSelf)
        {
            bomb.gameObject.SetActive(true);
        }

        // Re-apply after activate in case OnEnable raced.
        ForceState(bomb, hitCount, log, hideIfDetonated: false);
    }

    /// <summary>
    /// Pin a client bomb's rigged flag to the host's.
    ///
    /// Deliberately does NOT call Bomb.ConvertToRigged/ConvertFromRigged: both
    /// dereference the private <c>_animator</c>, which is only assigned in
    /// Bomb.OnEnable. The applier reaches bombs that are inactive (popped,
    /// parent group toggled off, freshly cloned), so the native calls throw a
    /// NullReferenceException on exactly the bombs that most need correcting.
    /// Setting the public field plus the controller off a live GetComponent
    /// works in every state, and matches what the native methods do.
    /// </summary>
    public static bool ForceRigged(Bomb bomb, bool rigged, ManualLogSource log = null)
    {
        if (bomb == null || bomb.isRigged == rigged)
        {
            return false;
        }

        bomb.isRigged = rigged;

        try
        {
            var controller = rigged ? bomb.riggedAnim : bomb.regularAnim;
            var animator = bomb.GetComponent<Animator>();
            if (animator != null && controller != null)
            {
                animator.runtimeAnimatorController = controller;
                animator.SetInteger(AnimHitsKey(bomb), bomb.HitCount);
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[BombSync] ForceRigged({rigged}) threw: {ex.Message}");
        }

        return true;
    }

    private static string AnimHitsKey(Bomb bomb)
    {
        try
        {
            return AnimHitsKeyField?.GetValue(bomb) as string ?? "NumHits";
        }
        catch
        {
            return "NumHits";
        }
    }
}
