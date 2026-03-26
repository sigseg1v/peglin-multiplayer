namespace PeglinMods.Spectator.Events.Handlers.Peg;

using PeglinMods.Spectator.Events.Network.Peg;

public sealed class PegHitServerHandler : IServerHandler<PegHitEvent>
{
    public PegHitEvent Handle(PegHitEvent networkEvent) => networkEvent;
}
