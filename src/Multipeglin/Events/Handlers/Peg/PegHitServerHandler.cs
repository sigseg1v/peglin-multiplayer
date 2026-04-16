namespace Multipeglin.Events.Handlers.Peg;

using Multipeglin.Events.Network.Peg;

public sealed class PegHitServerHandler : IServerHandler<PegHitEvent>
{
    public PegHitEvent Handle(PegHitEvent networkEvent) => networkEvent;
}
