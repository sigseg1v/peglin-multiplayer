namespace PeglinMods.Multiplayer.Events.Handlers.Peg;

using PeglinMods.Multiplayer.Events.Network.Peg;

public sealed class PegHitServerHandler : IServerHandler<PegHitEvent>
{
    public PegHitEvent Handle(PegHitEvent networkEvent) => networkEvent;
}
