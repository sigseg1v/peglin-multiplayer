using System.Collections.Generic;
using HarmonyLib;
using Multipeglin.Events;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class AnimatorPatches
{
    // =========================================================================
    // ANIMATION SYNC — capture enemy animator changes on host
    // =========================================================================

    // Per-Animator → Enemy lookup cache. SetTrigger / SetBool are postfixed
    // GAME-WIDE — every peg pop, particle emitter, damage number, UI tween
    // pays a GetComponentInParent<Enemy>() hierarchy walk per call. With
    // multiball + ~220 pegs that compounds badly. Cache the result (including
    // the "not an enemy" sentinel) per instance and use the dictionary on
    // subsequent calls.
    //
    // Use a Dictionary keyed by Animator instance, with a null value for
    // "checked, not an enemy". This trades a hierarchy walk for a hashtable
    // hit. Stale entries are tolerable — destroyed Animators become null
    // refs we drop on the next iteration; we re-poll periodically to bound
    // unbounded growth across scene transitions.
    private static readonly Dictionary<UnityEngine.Animator, Battle.Enemies.Enemy> _animatorEnemyCache
        = new Dictionary<UnityEngine.Animator, Battle.Enemies.Enemy>(64);

    private static int _animatorCacheCleanupCounter;

    private static bool TryResolveEnemyAnimator(UnityEngine.Animator animator, out Battle.Enemies.Enemy enemy)
    {
        if (_animatorEnemyCache.TryGetValue(animator, out enemy))
        {
            // Cached "not an enemy" → enemy is null and we early-out
            // Cached enemy may have been destroyed; null check below.
            if (enemy == null)
            {
                return false;
            }

            // gameObject test catches the case where the Enemy GameObject
            // was destroyed but the C# wrapper is still referenced.
            if (enemy.gameObject == null)
            {
                _animatorEnemyCache[animator] = null;
                return false;
            }

            return true;
        }

        enemy = animator.GetComponentInParent<Battle.Enemies.Enemy>();
        _animatorEnemyCache[animator] = enemy; // null is a valid sentinel ("checked, not enemy")

        // Periodically prune entries whose key Animator was destroyed to bound
        // dictionary growth across scene transitions. Cheap amortized scan.
        if ((++_animatorCacheCleanupCounter & 0xFF) == 0 && _animatorEnemyCache.Count > 256)
        {
            PruneDeadAnimators();
        }

        return enemy != null;
    }

    private static void PruneDeadAnimators()
    {
        List<UnityEngine.Animator> dead = null;
        foreach (var kvp in _animatorEnemyCache)
        {
            if (kvp.Key == null)
            {
                (dead ??= new List<UnityEngine.Animator>()).Add(kvp.Key);
            }
        }

        if (dead == null)
        {
            return;
        }

        foreach (var a in dead)
        {
            _animatorEnemyCache.Remove(a);
        }
    }

    // Cached service handles — set-once for plugin lifetime.
    private static IGameEventRegistry _cachedRegistry;
    private static Multipeglin.Utility.EnemyIdentifier _cachedEnemyId;

    private static bool TryGetServices(out IGameEventRegistry registry, out Multipeglin.Utility.EnemyIdentifier enemyId)
    {
        if (_cachedRegistry == null || _cachedEnemyId == null)
        {
            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                registry = null;
                enemyId = null;
                return false;
            }

            if (_cachedRegistry == null)
            {
                services.TryResolve(out _cachedRegistry);
            }

            if (_cachedEnemyId == null)
            {
                services.TryResolve(out _cachedEnemyId);
            }
        }

        registry = _cachedRegistry;
        enemyId = _cachedEnemyId;
        return registry != null && enemyId != null;
    }

    /// <summary>
    /// Capture Animator.SetTrigger calls on enemies and dispatch to client.
    /// This is a targeted hook — only fires when an Enemy's animator sets a trigger.
    /// </summary>
    [HarmonyPatch(typeof(UnityEngine.Animator), "SetTrigger", new[] { typeof(string) })]
    [HarmonyPostfix]
    public static void Animator_SetTrigger_Postfix(UnityEngine.Animator __instance, string name)
    {
        if (!IsHosting)
        {
            return;
        }

        if (__instance == null)
        {
            return;
        }

        // Only sync enemy animators. Cached lookup avoids per-call hierarchy walk.
        if (!TryResolveEnemyAnimator(__instance, out var enemy))
        {
            return;
        }

        if (!TryGetServices(out var registry, out var enemyId))
        {
            return;
        }

        var guid = enemyId.GetGuid(enemy);
        if (string.IsNullOrEmpty(guid))
        {
            return;
        }

        registry.Dispatch(new Multipeglin.Events.Network.Battle.AnimationSyncEvent
        {
            EntityGuid = guid,
            ParamType = "trigger",
            ParamName = name,
            PosX = enemy.transform.position.x,
            PosY = enemy.transform.position.y,
        });
    }

    /// <summary>Capture Animator.SetBool calls on enemies.</summary>
    [HarmonyPatch(typeof(UnityEngine.Animator), "SetBool", new[] { typeof(string), typeof(bool) })]
    [HarmonyPostfix]
    public static void Animator_SetBool_Postfix(UnityEngine.Animator __instance, string name, bool value)
    {
        if (!IsHosting)
        {
            return;
        }

        if (__instance == null)
        {
            return;
        }

        if (!TryResolveEnemyAnimator(__instance, out var enemy))
        {
            return;
        }

        if (!TryGetServices(out var registry, out var enemyId))
        {
            return;
        }

        var guid = enemyId.GetGuid(enemy);
        if (string.IsNullOrEmpty(guid))
        {
            return;
        }

        registry.Dispatch(new Multipeglin.Events.Network.Battle.AnimationSyncEvent
        {
            EntityGuid = guid,
            ParamType = "bool",
            ParamName = name,
            Value = value ? 1 : 0,
        });
    }
}
