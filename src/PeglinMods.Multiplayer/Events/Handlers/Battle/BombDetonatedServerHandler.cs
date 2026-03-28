namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class BombDetonatedServerHandler : IServerHandler<BombDetonatedEvent>
{
    public BombDetonatedEvent Handle(BombDetonatedEvent networkEvent) => networkEvent;
}
