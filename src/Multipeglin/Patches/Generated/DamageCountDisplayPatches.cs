using HarmonyLib;
using Multipeglin.Events;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class DamageCountDisplayPatches
{
    /// <summary>
    /// Capture damage text from host and dispatch to client.
    /// DamageCountDisplay.CreateText is called whenever a damage number appears.
    /// </summary>
    [HarmonyPatch(typeof(DamageCountDisplay), "CreateText")]
    [HarmonyPostfix]
    public static void DamageCountDisplay_CreateText_Postfix(string textOrLocKey, UnityEngine.Vector2 position, UnityEngine.Color color)
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

        registry.Dispatch(new Multipeglin.Events.Network.Battle.DamageTextEvent
        {
            Text = textOrLocKey,
            PosX = position.x,
            PosY = position.y,
            R = color.r,
            G = color.g,
            B = color.b,
            A = color.a,
        });
    }

    /// <summary>
    /// Block DamageCountDisplay on client — we'll render damage text from host events.
    /// </summary>
    [HarmonyPatch(typeof(DamageCountDisplay), "DisplayDamage")]
    [HarmonyPrefix]
    public static bool DamageCountDisplay_DisplayDamage_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }
}
