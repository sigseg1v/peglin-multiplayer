using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client handler for NavigateVoteEvent: never invoked on clients (server suppresses
/// rebroadcast). Stub exists so the event can register its server handler.
/// </summary>
public sealed class NavigateVoteClientHandler : IClientHandler<NavigateVoteEvent>
{
    public void Handle(NavigateVoteEvent networkEvent)
    {
        // No-op — vote events are host-only. Server handler returns null to suppress rebroadcast.
    }
}
