using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client handler for RewardChoicesEvent: stores choices so the UI can display a selection overlay.
/// Only processes the event if this client is the targeted player.
/// </summary>
public sealed class RewardChoicesClientHandler : IClientHandler<RewardChoicesEvent>
{
    public void Handle(RewardChoicesEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null) return;

        // Host→client event: host must never apply this to itself, and a forged
        // copy from another peer would otherwise pop a reward UI on this client.
        if (services.TryResolve<IMultiplayerMode>(out var mode) && mode.IsHosting)
            return;

        int mySlot = CoopSlotHelper.GetLocalSlotIndex(services);
        if (mySlot < 0) return;

        // Only process if this event targets our slot
        if (networkEvent.TargetSlotIndex != mySlot) return;

        MultiplayerPlugin.Logger?.LogInfo(
            $"[CoopReward] Received {networkEvent.Options?.Count ?? 0} reward options for slot {networkEvent.TargetSlotIndex}");

        CoopRewardState.PendingRewardChoices = networkEvent;
        CoopRewardState.WaitingForOtherPlayers = false;
        CoopRewardState.AllChoicesComplete = false;
    }
}
