using HarmonyLib;
using Multipeglin.Events;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class EnemyPatches
{
    [HarmonyPatch(typeof(Battle.Enemies.Enemy), "ApplyStatusEffect")]
    [HarmonyPrefix]
    public static bool Enemy_ApplyStatusEffect_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return AllowStatusEffectSync; // only allow when the applier is syncing
    }

    /// <summary>
    /// Capture the floating damage popup spawned over an enemy's head (during attack
    /// resolution) and forward it to clients as a DamageTextEvent. Enemies use a
    /// separate FloatingText pool, NOT DamageCountDisplay.CreateText, so they bypass
    /// the existing DamageText capture path. We rebuild the same formatted string and
    /// position the host uses, then dispatch so clients render an identical popup at
    /// the enemy's head via DamageCountDisplay.CreateText.
    /// </summary>
    [HarmonyPatch(typeof(global::Battle.Enemies.Enemy), "SpawnFloatingText")]
    [HarmonyPostfix]
    public static void Enemy_SpawnFloatingText_Postfix(
        global::Battle.Enemies.Enemy __instance,
        long damage,
        UnityEngine.Color color,
        global::Battle.Enemies.Enemy.EnemyDamageSource damageSource,
        float damageMod)
    {
        if (!IsHosting)
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

        var exampleField = typeof(global::Battle.Enemies.Enemy).GetField(
            "_exampleFloatingText",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var example = exampleField?.GetValue(__instance) as FloatingText;
        if (example == null)
        {
            return;
        }

        var pos = example.transform.position;

        var text = DamageCountDisplay.FormatDamageNumberAsString(damage);
        if (damageMod > 1f)
        {
            text = "<style=enemyBonusDmg>" + text + "x" + damageMod + "</style>";
        }
        else if (damageMod < 1f)
        {
            text = "<style=enemyNegDmg>" + text + "x" + damageMod + "</style>";
        }

        registry.Dispatch(new Multipeglin.Events.Network.Battle.DamageTextEvent
        {
            Text = text,
            PosX = pos.x,
            PosY = pos.y,
            R = color.r,
            G = color.g,
            B = color.b,
            A = color.a,
        });
    }

    [HarmonyPatch(typeof(Battle.Enemies.Enemy), "Damage")]
    [HarmonyPrefix]
    public static bool Enemy_Damage_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }
}
