using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class ChestScenarioControllerPatches
{
    /// <summary>
    /// Suppress the treasure-room "hit all bombs to spawn a bonus chest" easter
    /// egg in coop. The native flow reloads the Treasure scene only on whoever
    /// detonated the last bomb (the host, since clients can't shoot here), and
    /// the second chest reuses the treasure-phase reward gating that's already
    /// been marked complete — so the host opens a second chest the client never
    /// sees, both ends end up waiting on each other, and neither Force Skip nor
    /// natural completion can recover. Until we wire a real second-pass through
    /// CoopRewardState, just stop the bonus chest from triggering when a lobby
    /// is active.
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

        var current = (int)bombsRemainingField.GetValue(__instance);
        bombsRemainingField.SetValue(__instance, current - 1);
        MultiplayerPlugin.Logger?.LogInfo(
            $"[ClientPatch] Coop: bonus-chest bomb suppressed (remaining {current - 1})");
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
