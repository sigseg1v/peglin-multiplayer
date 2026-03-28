namespace PeglinMods.Multiplayer.Events.Handlers.Peg;

using PeglinMods.Multiplayer.Events.Network.Peg;

public sealed class PegDestroyedServerHandler : IServerHandler<PegDestroyedEvent>
{
    public PegDestroyedEvent Handle(PegDestroyedEvent networkEvent) => networkEvent;
}
