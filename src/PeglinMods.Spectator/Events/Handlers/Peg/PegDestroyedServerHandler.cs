namespace PeglinMods.Spectator.Events.Handlers.Peg;

using PeglinMods.Spectator.Events.Network.Peg;

public sealed class PegDestroyedServerHandler : IServerHandler<PegDestroyedEvent>
{
    public PegDestroyedEvent Handle(PegDestroyedEvent networkEvent) => networkEvent;
}
