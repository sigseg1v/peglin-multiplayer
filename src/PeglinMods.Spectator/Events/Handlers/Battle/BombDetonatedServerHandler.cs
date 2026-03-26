namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class BombDetonatedServerHandler : IServerHandler<BombDetonatedEvent>
{
    public BombDetonatedEvent Handle(BombDetonatedEvent networkEvent) => networkEvent;
}
