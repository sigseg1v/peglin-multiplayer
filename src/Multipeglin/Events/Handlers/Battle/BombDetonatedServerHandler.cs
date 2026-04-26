
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;
public sealed class BombDetonatedServerHandler : IServerHandler<BombDetonatedEvent>
{
    public BombDetonatedEvent Handle(BombDetonatedEvent networkEvent) => networkEvent;
}
