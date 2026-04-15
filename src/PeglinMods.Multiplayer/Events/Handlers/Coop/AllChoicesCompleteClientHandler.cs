using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

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

        // NOTE: for the shop phase we deliberately do NOT set AllChoicesComplete=true.
        // The host is now doing the post-shop navigation shot (still inside the
        // ShopScenario scene), and the client must keep its overlay up with a
        // different message until the scene actually changes. Dismissing the
        // overlay here would let the client re-interact with the shop UI.
        if (networkEvent.Phase != "shop")
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
        else if (networkEvent.Phase == "treasure")
        {
            CoopRewardState.TreasurePhaseActive = false;
            Patches.MultiplayerClientPatches.AllowTreasureLogic = false;
            Patches.MultiplayerClientPatches.AllowRelicSync = false;
            CoopRewardState.ClientTreasureChoiceSent = false;
            MultiplayerPlugin.Logger?.LogInfo("[CoopReward] Treasure phase ended");
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
