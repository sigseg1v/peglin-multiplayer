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

        // Only sync enemy animators (check if this animator belongs to an Enemy)
        var enemy = __instance.GetComponentInParent<Battle.Enemies.Enemy>();
        if (enemy == null)
        {
            return;
        }

        if (MultiplayerPlugin.Services == null)
        {
            return;
        }

        if (!MultiplayerPlugin.Services.TryResolve<IGameEventRegistry>(out var registry))
        {
            return;
        }

        if (!MultiplayerPlugin.Services.TryResolve<Multipeglin.Utility.EnemyIdentifier>(out var enemyId))
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

        var enemy = __instance.GetComponentInParent<Battle.Enemies.Enemy>();
        if (enemy == null)
        {
            return;
        }

        if (MultiplayerPlugin.Services == null)
        {
            return;
        }

        if (!MultiplayerPlugin.Services.TryResolve<IGameEventRegistry>(out var registry))
        {
            return;
        }

        if (!MultiplayerPlugin.Services.TryResolve<Multipeglin.Utility.EnemyIdentifier>(out var enemyId))
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
