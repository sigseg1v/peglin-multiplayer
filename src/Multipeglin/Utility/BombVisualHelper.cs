using System;
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
///
/// See wiki/plans/bomb-hit-state-sync.md.
/// </summary>
public static class BombVisualHelper
{
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
            bomb.HitCount = hitCount;

            var animator = bomb.GetComponent<Animator>();
            if (animator == null)
            {
                animator = AccessTools.Field(typeof(Bomb), "_animator")?.GetValue(bomb) as Animator;
            }

            var animKey = AccessTools.Field(typeof(Bomb), "_animHitsKey")?.GetValue(bomb) as string
                ?? "NumHits";
            animator?.SetInteger(animKey, hitCount);

            var collider = AccessTools.Field(typeof(Peg), "_collider")?.GetValue(bomb) as Collider2D;
            var untouched = AccessTools.Field(typeof(Bomb), "_untouchedMaterial")?.GetValue(bomb) as PhysicsMaterial2D;
            var explode = AccessTools.Field(typeof(Bomb), "_explodeMaterial")?.GetValue(bomb) as PhysicsMaterial2D;
            var detonatedField = AccessTools.Field(typeof(Bomb), "_detonated");
            var detonatedThisTurnField = AccessTools.Field(typeof(Bomb), "_detonatedThisTurn");

            if (hitCount <= 0)
            {
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

                if (hideIfDetonated && bomb.gameObject.activeSelf)
                {
                    bomb.gameObject.SetActive(false);
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
        if (bomb.gameObject.activeSelf)
        {
            bomb.gameObject.SetActive(false);
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
}
