using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Server handler for NavigateResolvedEvent (host -> clients). Pure passthrough.
/// Host-side state is updated by CoopNavigateResolver before Dispatch.
/// </summary>
public sealed class NavigateResolvedServerHandler : IServerHandler<NavigateResolvedEvent>
{
    public NavigateResolvedEvent Handle(NavigateResolvedEvent networkEvent) => networkEvent;
}
