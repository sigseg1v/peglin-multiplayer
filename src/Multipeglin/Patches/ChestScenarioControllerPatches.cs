using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class ChestScenarioControllerPatches
{
    /// <summary>
    /// Coop-sync the treasure-room "hit all bombs to spawn a bonus chest" easter
    /// egg. The native flow reloads the Treasure scene only on whoever detonated
    /// the last bomb, bypassing the parallel-shoot navigate phase entirely —
    /// everyone else stays desynced in the navigation screen (issue #3). Instead,
    /// count detonations locally (real + ghost balls both pop pegs here) and when
    /// the counter hits zero route the transition through BonusChestSync so the
    /// host moves ALL players into the bonus chest room together.
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.ChestScenarioController), "HandlePegDestruction")]
    [HarmonyPrefix]
    public static bool ChestScenarioController_HandlePegDestruction_Prefix(
        Scenarios.ChestScenarioController __instance,
        Peg.PegType type)
    {
        if (!UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        if (type != Peg.PegType.BOMB)
        {
            return true;
        }

        var bombsRemainingField = AccessTools.Field(
            typeof(Scenarios.ChestScenarioController), "_bombsRemaining");
        if (bombsRemainingField == null)
        {
            return true;
        }

        // Mirror the native decrement, but never let the native single-player
        // transition run — BonusChestSync owns the coop transition.
        var remaining = (int)bombsRemainingField.GetValue(__instance) - 1;
        bombsRemainingField.SetValue(__instance, remaining);
        MultiplayerPlugin.Logger?.LogInfo($"[BonusChest] Bomb detonated, {remaining} remaining");

        var isBonusChestField = AccessTools.Field(
            typeof(Scenarios.ChestScenarioController), "_isBonusChest");
        var isBonusChest = (bool?)isBonusChestField?.GetValue(__instance) ?? false;

        if (remaining == 0
            && Scenarios.ChestScenarioController.bombChestsOpened < 1
            && !isBonusChest)
        {
            Events.Handlers.Scenarios.BonusChestSync.LocalPlayerTriggered("chest");
        }

        return false;
    }

    /// <summary>
    /// Same easter egg, scenario-pegboard variant (ScenarioNavigationBonusChest,
    /// the "store robbed" bonus). Native immediately FadeAndLoads TREASURE on the
    /// local detonator only — route through BonusChestSync instead.
    /// </summary>
    [HarmonyPatch(typeof(RNG.Scenarios.ScenarioNavigationBonusChest), "HandlePegDestruction")]
    [HarmonyPrefix]
    public static bool ScenarioNavigationBonusChest_HandlePegDestruction_Prefix(
        RNG.Scenarios.ScenarioNavigationBonusChest __instance,
        Peg.PegType type)
    {
        if (!UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        if (type != Peg.PegType.BOMB)
        {
            return true;
        }

        var bombsRemainingField = AccessTools.Field(
            typeof(RNG.Scenarios.ScenarioNavigationBonusChest), "_bombsRemaining");
        if (bombsRemainingField == null)
        {
            return true;
        }

        var remaining = (int)bombsRemainingField.GetValue(__instance) - 1;
        bombsRemainingField.SetValue(__instance, remaining);
        MultiplayerPlugin.Logger?.LogInfo($"[BonusChest] Scenario bomb detonated, {remaining} remaining");

        if (remaining == 0)
        {
            Events.Handlers.Scenarios.BonusChestSync.LocalPlayerTriggered("store_robbed");
        }

        return false;
    }

    [HarmonyPatch(typeof(Scenarios.ChestScenarioController), "OpenChest")]
    [HarmonyPrefix]
    public static bool ChestScenarioController_OpenChest_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (AllowTreasureLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked ChestScenarioController.OpenChest on client");
        return false;
    }

    /// <summary>
    /// ChestScenarioController.Skip — controls navigation after treasure relic selection.
    /// On client: send TreasureCompleteEvent, block navigation.
    /// On host: check wait-for-all before allowing navigation.
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.ChestScenarioController), "Skip")]
    [HarmonyPrefix]
    public static bool ChestScenarioController_Skip_Prefix(Scenarios.ChestScenarioController __instance)
    {
        if (!UI.LobbyUI.GameStartReceived)
        {
            return true; // Not in coop
        }

        if (ShouldSuppressClientLogic)
        {
            // If the treasure phase already completed (host moved on, nav phase
            // already started), do nothing — just block. Without this guard the
            // chest's Skip-tween coroutine can fire after CoopRewardUI.Reset()
            // clears ClientTreasureChoiceSent, causing a duplicate
            // TreasureCompleteEvent AND re-setting WaitingForOtherPlayers=true
            // which leaves the overlay stuck on top of the nav playfield.
            if (Events.Handlers.Coop.CoopRewardState.AllChoicesComplete
                || Events.Handlers.Coop.CoopNavigateState.PhaseActive
                || Events.Handlers.Coop.CoopNavigateState.Resolved)
            {
                AllowTreasureLogic = false;
                return false;
            }

            // CLIENT: send treasure complete if not already sent
            if (!Events.Handlers.Coop.CoopRewardState.ClientTreasureChoiceSent)
            {
                try
                {
                    // If we get here without AcceptRelic having fired, the player skipped
                    var services = MultiplayerPlugin.Services;
                    if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
                    {
                        sender.Send(new Events.Network.Scenarios.TreasureCompleteEvent
                        {
                            ChosenRelicEffect = -1,
                            ChosenRelicName = null,
                        });
                        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Client skipped treasure relic — sent TreasureCompleteEvent");
                    }

                    Events.Handlers.Coop.CoopRewardState.ClientTreasureChoiceSent = true;
                }
                catch (System.Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogError($"[ClientPatch] Failed to send TreasureCompleteEvent: {ex.Message}");
                }
            }

            AllowTreasureLogic = false;
            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
            Events.Handlers.Coop.CoopRewardState.TreasurePhaseActive = true;
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Client finished treasure — waiting for other players");
            return false; // Block navigation on client
        }

        if (IsHosting && Events.Handlers.Coop.CoopRewardState.TreasurePhaseActive)
        {
            // HOST: mark self as done, check if all clients finished
            Events.Handlers.Coop.CoopRewardState.HostTreasureDone = true;

            if (Events.Handlers.Coop.CoopRewardState.AllClientTreasureChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host Skip — all clients done, proceeding");
                Events.Handlers.Coop.CoopRewardState.TreasurePhaseActive = false;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = false;
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Events.IGameEventRegistry>(out var reg) == true)
                {
                    reg.Dispatch(new Events.Network.Coop.AllChoicesCompleteEvent { Phase = "treasure" });
                }

                return true; // Let Skip run normally
            }
            else
            {
                Events.Handlers.Coop.CoopRewardState.PendingChestController = __instance;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host Skip — waiting for other players to finish treasure");
                return false; // Block until all clients done
            }
        }

        return true;
    }
}
