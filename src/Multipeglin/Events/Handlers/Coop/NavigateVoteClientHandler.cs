using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Receive-side handler for NavigateVoteEvent. Despite the "Client" suffix in
/// the type name, GameEventRegistry.HandleIncoming dispatches to *this* handler
/// on whichever side receives the bytes — so on the host this is where the
/// vote actually lands. (The matching ServerHandler only runs via Dispatch(),
/// and we never Dispatch a NavigateVoteEvent.) Same convention as
/// ShootRequestClientHandler.
/// </summary>
public sealed class NavigateVoteClientHandler : IClientHandler<NavigateVoteEvent>
{
    public void Handle(NavigateVoteEvent networkEvent)
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

        if (!services.TryResolve<PlayerRegistry>(out var playerRegistry))
        {
            return;
        }

        var senderPeerId = (services.TryResolve<GameEventRegistry>(out var reg) ? reg : null)?.CurrentSenderPeerId ?? -1;
        var slot = playerRegistry.GetSlotByPeerId(senderPeerId);
        if (slot == null)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[CoopNavigate] Vote from unknown peer {senderPeerId} — ignoring");
            return;
        }

        CoopNavigateResolver.RecordClientVote(slot.SlotIndex, networkEvent.ChildIndex);
    }
}
