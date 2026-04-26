using Multipeglin.Events.Network.Peg;

namespace Multipeglin.Events.Handlers.Peg;

public sealed class PegHitServerHandler : IServerHandler<PegHitEvent>
{
    public PegHitEvent Handle(PegHitEvent networkEvent) => networkEvent;
}
