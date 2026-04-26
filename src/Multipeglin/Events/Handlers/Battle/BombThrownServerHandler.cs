using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class BombThrownServerHandler : IServerHandler<BombThrownEvent>
{
    public BombThrownEvent Handle(BombThrownEvent networkEvent) => networkEvent;
}
