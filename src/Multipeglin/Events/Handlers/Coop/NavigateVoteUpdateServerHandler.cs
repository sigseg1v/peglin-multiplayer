using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Server handler for NavigateVoteUpdateEvent (host -> clients). Pure passthrough.
/// </summary>
public sealed class NavigateVoteUpdateServerHandler : IServerHandler<NavigateVoteUpdateEvent>
{
    public NavigateVoteUpdateEvent Handle(NavigateVoteUpdateEvent networkEvent) => networkEvent;
}
