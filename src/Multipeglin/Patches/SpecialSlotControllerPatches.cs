using System;
using Battle;
using HarmonyLib;
using Multipeglin.Events;
using Multipeglin.Events.Network.Coop;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class SpecialSlotControllerPatches
{
    // =========================================================================
    // HOST: WEIGHTED CHIP (SLOT_MULTIPLIERS) — SKIP DURING CLIENT TURNS
    // =========================================================================

    /// <summary>
    /// Weighted Chip adds multiplier zones (0.5x, 1x, 2x) and fire pits at the
    /// bottom of the pegboard. These belong to the host's relic set — they should
    /// only affect the host's shots, not the client's.
    /// During client turns: skip both the damage multiplier and fire pit damage.
    /// Still allow inhale zone activation (separate relic mechanic).
    /// </summary>
    [HarmonyPatch(typeof(SpecialSlotController), "SlotActivated")]
    [HarmonyPrefix]
    public static bool SpecialSlotController_SlotActivated_Prefix(
        int index,
        BattleController ____battleController,
        int ____inhaleSlot)
    {
        if (!IsHosting)
        {
            return true;
        }

        if (!UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        if (Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn)
        {
            return true;
        }

        // Client's turn — skip multiplier + fire pit damage from host's Weighted Chip.
        // Still handle inhale zone (separate relic) if applicable.
        if (____battleController != null && !____battleController.IsNavigating()
            && index == ____inhaleSlot)
        {
            SpecialSlotController.OrbInhaled?.Invoke();
        }

        return false;
    }

    // =========================================================================
    // TURN-COMPLETE SHUFFLE SYNC — Pumpkin Pi (SLOT_PORTAL) + SLOT_MULTIPLIERS
    // =========================================================================

    /// <summary>
    /// TurnComplete shuffles _slotMultipliersRelicAmounts via unseeded
    /// UnityEngine.Random — host and client diverge on which slot gets the
    /// portal / 2x / 0.5x marker. Block the local run on clients; the host
    /// captures and broadcasts the applied state via SlotConfigEvent.
    /// </summary>
    [HarmonyPatch(typeof(SpecialSlotController), "TurnComplete")]
    [HarmonyPrefix]
    public static bool SpecialSlotController_TurnComplete_Prefix()
    {
        return !ShouldSuppressClientLogic;
    }

    [HarmonyPatch(typeof(SpecialSlotController), "TurnComplete")]
    [HarmonyPostfix]
    public static void SpecialSlotController_TurnComplete_Postfix(SpecialSlotController __instance)
    {
        if (!IsHosting)
        {
            return;
        }

        if (__instance == null || __instance.slotTriggers == null)
        {
            return;
        }

        try
        {
            var triggers = __instance.slotTriggers;
            var n = triggers.Length;
            var mults = new float[n];
            var portals = new bool[n];
            var flames = new bool[n];

            var portalField = AccessTools.Field(typeof(SlotTrigger), "_isPortal");
            for (var i = 0; i < n; i++)
            {
                var t = triggers[i];
                if (t == null)
                {
                    mults[i] = 1f;
                    continue;
                }

                mults[i] = t.multiplier;
                flames[i] = t.damageOnEnter;
                portals[i] = portalField != null && (bool)portalField.GetValue(t);
            }

            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IGameEventRegistry>(out var reg) == true)
            {
                reg.Dispatch(new SlotConfigEvent
                {
                    Multipliers = mults,
                    PortalsOn = portals,
                    FlamesOn = flames,
                });
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SlotConfig] capture failed: {ex.Message}");
        }
    }
}
