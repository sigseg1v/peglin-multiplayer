using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class ChestScenarioControllerPatches
{
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
