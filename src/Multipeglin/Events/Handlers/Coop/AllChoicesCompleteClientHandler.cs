using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client handler for AllChoicesCompleteEvent: signals that all players
/// have finished their selection and the game can proceed.
/// </summary>
public sealed class AllChoicesCompleteClientHandler : IClientHandler<AllChoicesCompleteEvent>
{
    public void Handle(AllChoicesCompleteEvent networkEvent)
    {
        MultiplayerPlugin.Logger?.LogInfo(
            $"[CoopReward] All choices complete for phase '{networkEvent.Phase}'");

        // NOTE: for shop/text_scenario/treasure phases we deliberately do NOT set
        // AllChoicesComplete=true. The host is now doing the post-event navigation
        // shot (still inside the ShopScenario/TextScenario/Treasure scene), and the
        // client must keep its overlay up with a "Waiting for host..." message until
        // the scene actually changes. Dismissing the overlay here would let the client
        // re-interact with the event UI or leave them staring at a blank scene
        // with no indicator that they're waiting.
        if (networkEvent.Phase != "shop" && networkEvent.Phase != "text_scenario"
            && networkEvent.Phase != "treasure")
        {
            CoopRewardState.AllChoicesComplete = true;
            CoopRewardState.WaitingForOtherPlayers = false;
        }

        // Clear native reward phase state on client
        if (networkEvent.Phase == "post_battle")
        {
            CoopRewardState.ClientInNativeRewardPhase = false;
            CoopRewardState.PendingPostBattleRelicChoices = null;
            Patches.MultiplayerClientPatches.AllowNativeRewardLogic = false;
            Patches.MultiplayerClientPatches.ClientChosenPostBattleRelicEffect = -1;
            Patches.MultiplayerClientPatches.ClientChosenPostBattleRelicName = null;
            MultiplayerPlugin.Logger?.LogInfo("[CoopReward] Post-battle reward phase ended on client");
        }
        else if (networkEvent.Phase == "shop")
        {
            // Keep the overlay up but switch to the "waiting for host nav" message.
            CoopRewardState.ShopPhaseActive = false;
            CoopRewardState.ShopAwaitingHostNavigation = true;
            CoopRewardState.WaitingForOtherPlayers = true;
            Patches.MultiplayerClientPatches.AllowShopLogic = false;
            MultiplayerPlugin.Logger?.LogInfo("[CoopReward] Shop phase ended — awaiting host navigation shot");
        }
        else if (networkEvent.Phase == "text_scenario")
        {
            // Keep the overlay up — host is still inside TextScenario doing the
            // post-event navigation shot to pick the next stage on the map.
            CoopRewardState.TextScenarioPhaseActive = false;
            CoopRewardState.TextScenarioAwaitingHostNavigation = true;
            CoopRewardState.WaitingForOtherPlayers = true;
            Patches.MultiplayerClientPatches.AllowTextScenarioLogic = false;
            MultiplayerPlugin.Logger?.LogInfo("[CoopReward] TextScenario phase ended — awaiting host navigation shot");
        }
        else if (networkEvent.Phase == "treasure")
        {
            // Keep the overlay up — host is still inside Treasure doing the
            // post-selection navigation shot at the chest/pegboard.
            CoopRewardState.TreasurePhaseActive = false;
            CoopRewardState.TreasureAwaitingHostNavigation = true;
            CoopRewardState.WaitingForOtherPlayers = true;
            Patches.MultiplayerClientPatches.AllowTreasureLogic = false;
            Patches.MultiplayerClientPatches.AllowRelicSync = false;
            CoopRewardState.ClientTreasureChoiceSent = false;
            MultiplayerPlugin.Logger?.LogInfo("[CoopReward] Treasure phase ended — awaiting host navigation shot");
        }
        else if (networkEvent.Phase == "peg_minigame")
        {
            CoopRewardState.PegMinigamePhaseActive = false;
            Patches.MultiplayerClientPatches.AllowPegMinigameLogic = false;
            CoopRewardState.ClientPegMinigameChoiceSent = false;
            MultiplayerPlugin.Logger?.LogInfo("[CoopReward] PegMinigame phase ended");
        }
    }
}
