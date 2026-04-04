using PeglinMods.Multiplayer.Events.Network.Coop;
using PeglinMods.Multiplayer.Multiplayer;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Client handler for RelicChoicesEvent: stores choices so the UI can display a selection overlay.
/// Only processes the event if this client is the targeted player.
/// </summary>
public sealed class RelicChoicesClientHandler : IClientHandler<RelicChoicesEvent>
{
    public void Handle(RelicChoicesEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null) return;

        int mySlot = CoopSlotHelper.GetLocalSlotIndex(services);
        if (mySlot < 0) return;

        // Only process if this event targets our slot
        if (networkEvent.TargetSlotIndex != mySlot) return;

        MultiplayerPlugin.Logger?.LogInfo(
            $"[CoopReward] Received {networkEvent.Choices?.Count ?? 0} relic choices for slot {networkEvent.TargetSlotIndex}");

        CoopRewardState.PendingRelicChoices = networkEvent;
        CoopRewardState.WaitingForOtherPlayers = false;
        CoopRewardState.AllChoicesComplete = false;
    }
}
