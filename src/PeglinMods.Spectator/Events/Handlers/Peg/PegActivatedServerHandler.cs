namespace PeglinMods.Spectator.Events.Handlers.Peg;

using PeglinMods.Spectator.Events.Network.Peg;

public sealed class PegActivatedServerHandler : IServerHandler<PegActivatedEvent>
{
    public PegActivatedEvent Handle(PegActivatedEvent networkEvent) => networkEvent;
}
