
using Multipeglin.Events.Network.Peg;

namespace Multipeglin.Events.Handlers.Peg;
public sealed class PegDestroyedServerHandler : IServerHandler<PegDestroyedEvent>
{
    public PegDestroyedEvent Handle(PegDestroyedEvent networkEvent) => networkEvent;
}
