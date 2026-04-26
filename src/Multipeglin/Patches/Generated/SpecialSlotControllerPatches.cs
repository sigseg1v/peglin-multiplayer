using Battle;
using HarmonyLib;
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
}
