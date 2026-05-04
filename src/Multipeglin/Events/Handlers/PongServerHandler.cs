using Multipeglin.Events.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>No-op — host never receives PongEvent (pong flows host → client only).</summary>
public sealed class PongServerHandler : IServerHandler<PongEvent>
{
    public PongEvent Handle(PongEvent networkEvent) => null;
}
