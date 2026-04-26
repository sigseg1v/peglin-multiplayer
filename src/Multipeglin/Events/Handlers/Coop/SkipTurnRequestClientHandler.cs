using System;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Runs on the host when a client sends SkipTurnRequestEvent. Validates the
/// sender is the current active turn player, then invokes the skip-turn flow
/// on CoopSubscriptions (records zero damage, advances TurnManager).
/// </summary>
public sealed class SkipTurnRequestClientHandler : IClientHandler<SkipTurnRequestEvent>
{
    public void Handle(SkipTurnRequestEvent networkEvent)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                return;
            }

            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting)
            {
                return;
            }

            var registry = services.TryResolve<IGameEventRegistry>(out var reg) ? reg : null;
            var senderPeerId = (registry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;

            if (!services.TryResolve<PlayerRegistry>(out var playerRegistry))
            {
                return;
            }

            var slot = playerRegistry.GetSlotByPeerId(senderPeerId);
            if (slot == null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[SkipTurn] From unknown peer {senderPeerId}");
                return;
            }

            var subs = Subscriptions.CoopSubscriptions.Instance;
            if (subs == null)
            {
                MultiplayerPlugin.Logger?.LogWarning("[SkipTurn] CoopSubscriptions.Instance null — cannot skip");
                return;
            }

            subs.SkipCurrentTurn(slot.SlotIndex, $"client slot {slot.SlotIndex}");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SkipTurn] Handler failed: {ex.Message}");
        }
    }
}
