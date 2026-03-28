namespace PeglinMods.Multiplayer.Events.Handlers.Peg;

using PeglinMods.Multiplayer.Events.Network.Peg;

public sealed class PegActivatedServerHandler : IServerHandler<PegActivatedEvent>
{
    public PegActivatedEvent Handle(PegActivatedEvent networkEvent) => networkEvent;
}
