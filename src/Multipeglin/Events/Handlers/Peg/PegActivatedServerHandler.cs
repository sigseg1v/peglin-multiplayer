namespace Multipeglin.Events.Handlers.Peg;

using Multipeglin.Events.Network.Peg;

public sealed class PegActivatedServerHandler : IServerHandler<PegActivatedEvent>
{
    public PegActivatedEvent Handle(PegActivatedEvent networkEvent) => networkEvent;
}
