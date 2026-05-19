using System;
using System.Linq;
using Battle;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.Events;
using Multipeglin.Events.Network.Coop;
using Relics;
using UnityEngine;
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

    /// <summary>
    /// Re-toggle SpecialSlotController slot triggers so SLOT_PORTAL (Pumpkin Pi)
    /// reflects the currently loaded RelicManager. SpecialSlotController.TurnComplete
    /// only runs once per round, so without this hook a portal placed by Player A's
    /// Pumpkin Pi at end-of-round R would carry over to Player B's turn (or be
    /// missing entirely from B's turn if A didn't have the relic).
    /// </summary>
    public static void ReconfigurePortalsForActiveRelics(ManualLogSource log = null)
    {
        try
        {
            var ssc = UnityEngine.Object.FindObjectOfType<SpecialSlotController>();
            if (ssc == null || ssc.slotTriggers == null || ssc.slotTriggers.Length == 0)
            {
                return;
            }

            var relicMgr = Resources.FindObjectsOfTypeAll<RelicManager>()?.FirstOrDefault();
            if (relicMgr == null)
            {
                return;
            }

            // Pumpkin Pi (SLOT_PORTAL) is shared across the coop run — if any player
            // owns it, the portal is active on every player's turn. The live
            // RelicManager only reflects the currently swapped-in player's relics, so
            // check every CoopPlayerState's stored relics for the effect.
            var portalActive = relicMgr.RelicEffectActive(RelicEffect.SLOT_PORTAL);
            if (!portalActive)
            {
                var services2 = MultiplayerPlugin.Services;
                if (services2?.TryResolve<GameState.CoopStateManager>(out var coopMgr) == true)
                {
                    foreach (var kvp in coopMgr.PlayerStates)
                    {
                        var owned = kvp.Value?.OwnedRelics;
                        if (owned == null)
                        {
                            continue;
                        }

                        foreach (var sr in owned)
                        {
                            if (sr != null && sr.Effect == (int)RelicEffect.SLOT_PORTAL)
                            {
                                portalActive = true;
                                break;
                            }
                        }

                        if (portalActive)
                        {
                            break;
                        }
                    }
                }
            }

            var amountsField = AccessTools.Field(typeof(SpecialSlotController), "_slotMultipliersRelicAmounts");
            var amounts = amountsField?.GetValue(ssc) as int[];
            var bottomColorField = AccessTools.Field(typeof(SpecialSlotController), "bottomPortalColor");
            var bottomColor = (Color)(bottomColorField?.GetValue(ssc) ?? Color.magenta);

            var triggers = ssc.slotTriggers;
            var portalsOn = new bool[triggers.Length];
            var mults = new float[triggers.Length];
            var flames = new bool[triggers.Length];

            for (var k = 0; k < triggers.Length; k++)
            {
                if (triggers[k] == null)
                {
                    mults[k] = 1f;
                    continue;
                }

                var shouldBePortal = portalActive
                    && amounts != null
                    && k < amounts.Length
                    && amounts[k] == -1;

                triggers[k].TogglePortal(shouldBePortal, bottomColor);

                portalsOn[k] = shouldBePortal;
                mults[k] = triggers[k].multiplier;
                flames[k] = triggers[k].damageOnEnter;
            }

            // Re-broadcast to clients so their visuals + AwaitingShotCompletion
            // portal state stay in sync with the new active player's relics.
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IGameEventRegistry>(out var reg) == true)
            {
                reg.Dispatch(new SlotConfigEvent
                {
                    Multipliers = mults,
                    PortalsOn = portalsOn,
                    FlamesOn = flames,
                });
            }

            log?.LogInfo($"[SlotConfig] Reconfigured portals for active relics: " +
                $"SLOT_PORTAL={portalActive}, portalSlots=[{string.Join(",", portalsOn)}]");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SlotConfig] reconfigure failed: {ex.Message}");
        }
    }

    // =========================================================================
    // DIAGNOSTIC: LOG SLOT TRIGGER ENTRIES (host-side)
    // =========================================================================

    // cached: AccessTools.Field is uncached. OnTriggerEnter2D fires for every
    // peg-and-slot collision the ball makes during a shot — multiple times per
    // shot per slot. Burning 2 reflection lookups + 2 reflective .GetValue
    // calls per collision purely for a debug log is wasteful.
    private static readonly System.Reflection.FieldInfo _slotPortalField
        = AccessTools.Field(typeof(SlotTrigger), "_isPortal");

    private static readonly System.Reflection.FieldInfo _slotUsageField
        = AccessTools.Field(typeof(SlotTrigger), "_portalUsageCount");

    /// <summary>
    /// Logs every ball entry into a SlotTrigger so we can diagnose Pumpkin Pi
    /// portal teleports during coop client turns.
    /// </summary>
    [HarmonyPatch(typeof(SlotTrigger), "OnTriggerEnter2D")]
    [HarmonyPrefix]
    public static void SlotTrigger_OnTriggerEnter2D_Prefix(SlotTrigger __instance, Collider2D collision)
    {
        try
        {
            if (!IsHosting || !UI.LobbyUI.GameStartReceived)
            {
                return;
            }

            var ball = collision?.GetComponent<PachinkoBall>();
            if (ball == null)
            {
                return;
            }

            var isPortal = _slotPortalField != null && (bool)(_slotPortalField.GetValue(__instance) ?? false);
            var usage = _slotUsageField != null ? (int)(_slotUsageField.GetValue(__instance) ?? 0) : -1;
            var awaiting = BattleController.AwaitingShotCompletion();
            var willTeleport = awaiting && isPortal && usage < 3;

            MultiplayerPlugin.Logger?.LogDebug(
                $"[PortalDiag] slot={__instance.index} isPortal={isPortal} " +
                $"usage={usage} awaiting={awaiting} mult={__instance.multiplier} " +
                $"flame={__instance.damageOnEnter} ballPos={ball.transform.position} " +
                $"willTeleport={willTeleport} myTurn={Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn}");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[PortalDiag] log failed: {ex.Message}");
        }
    }
}
