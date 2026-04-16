namespace Multipeglin.Events.Handlers.Peg;

using Multipeglin.Events.Network.Peg;

public sealed class PegDestroyedServerHandler : IServerHandler<PegDestroyedEvent>
{
    public PegDestroyedEvent Handle(PegDestroyedEvent networkEvent) => networkEvent;
}
