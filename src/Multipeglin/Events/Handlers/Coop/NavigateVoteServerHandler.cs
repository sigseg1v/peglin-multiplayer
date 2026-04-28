using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// ServerHandler stub. The vote-recording happens on receive — see
/// NavigateVoteClientHandler. This handler is only reachable via
/// IGameEventRegistry.Dispatch(), which we never call for NavigateVoteEvent
/// (clients use IMessageSender.Send to deliver the bytes directly to the host).
/// Returning null suppresses the rebroadcast that Dispatch would otherwise do.
/// </summary>
public sealed class NavigateVoteServerHandler : IServerHandler<NavigateVoteEvent>
{
    public NavigateVoteEvent Handle(NavigateVoteEvent networkEvent)
    {
        return null;
    }
}
