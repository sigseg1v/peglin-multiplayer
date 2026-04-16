namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class BombDetonatedServerHandler : IServerHandler<BombDetonatedEvent>
{
    public BombDetonatedEvent Handle(BombDetonatedEvent networkEvent) => networkEvent;
}
