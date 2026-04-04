using System;
using PeglinMods.Multiplayer.Events.Network.Coop;
using PeglinMods.Multiplayer.Multiplayer;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Client handler for RelicChoiceEvent: only the host processes this
/// (a client's relic selection arriving at the host).
/// </summary>
public sealed class RelicChoiceClientHandler : IClientHandler<RelicChoiceEvent>
{
    public void Handle(RelicChoiceEvent networkEvent)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null) return;

            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting) return;

            var eventRegistry = services.TryResolve<IGameEventRegistry>(out var reg) ? reg : null;
            var senderPeerId = (eventRegistry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;

            if (!services.TryResolve<PlayerRegistry>(out var registry)) return;
            var slot = registry.GetSlotByPeerId(senderPeerId);
            if (slot == null)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopReward] RelicChoice from unknown peer {senderPeerId}");
                return;
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopReward] Player '{slot.PlayerName}' (slot {slot.SlotIndex}) chose relic effect {networkEvent.ChosenRelicEffect}");

            // The CoopStateManager or a reward orchestrator will pick this up
            // and apply the relic to the player's state, then check if all players have chosen
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] RelicChoice handler failed: {e.Message}");
        }
    }
}
