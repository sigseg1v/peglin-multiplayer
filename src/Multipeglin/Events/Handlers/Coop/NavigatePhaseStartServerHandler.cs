using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Server handler for NavigatePhaseStartEvent (host -> clients).
/// Pure passthrough — host-side state is initialized by CoopNavigateResolver.StartPhase
/// before Dispatch is called.
/// </summary>
public sealed class NavigatePhaseStartServerHandler : IServerHandler<NavigatePhaseStartEvent>
{
    public NavigatePhaseStartEvent Handle(NavigatePhaseStartEvent networkEvent) => networkEvent;
}
