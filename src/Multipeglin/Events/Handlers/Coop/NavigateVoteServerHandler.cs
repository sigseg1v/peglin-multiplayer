using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Server handler for NavigateVoteEvent (client -> host). Records the vote via
/// CoopNavigateResolver and suppresses rebroadcast (returns null). The host
/// rebroadcasts the tally via NavigateVoteUpdateEvent and the final winner via
/// NavigateResolvedEvent — clients never receive raw NavigateVoteEvents.
/// </summary>
public sealed class NavigateVoteServerHandler : IServerHandler<NavigateVoteEvent>
{
    public NavigateVoteEvent Handle(NavigateVoteEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return null;
        }

        var registry = services.TryResolve<GameEventRegistry>(out var reg) ? reg : null;
        var senderPeerId = registry?.CurrentSenderPeerId ?? -1;

        if (!services.TryResolve<PlayerRegistry>(out var playerRegistry))
        {
            return null;
        }

        var slot = playerRegistry.GetSlotByPeerId(senderPeerId);
        if (slot == null)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[CoopNavigate] Vote from unknown peer {senderPeerId} — ignoring");
            return null;
        }

        CoopNavigateResolver.RecordClientVote(slot.SlotIndex, networkEvent.ChildIndex);
        return null;
    }
}
