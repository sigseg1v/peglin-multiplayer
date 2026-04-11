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

        CoopRewardState.AllChoicesComplete = true;
        CoopRewardState.WaitingForOtherPlayers = false;

        // Clear native reward phase state on client
        if (networkEvent.Phase == "post_battle")
        {
            CoopRewardState.ClientInNativeRewardPhase = false;
            Patches.MultiplayerClientPatches.AllowNativeRewardLogic = false;
            MultiplayerPlugin.Logger?.LogInfo("[CoopReward] Post-battle reward phase ended on client");
        }
    }
}
