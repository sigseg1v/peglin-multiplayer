
using Multipeglin.Events.Network.Peg;

namespace Multipeglin.Events.Handlers.Peg;
public sealed class PegActivatedServerHandler : IServerHandler<PegActivatedEvent>
{
    public PegActivatedEvent Handle(PegActivatedEvent networkEvent) => networkEvent;
}
