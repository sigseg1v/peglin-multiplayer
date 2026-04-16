namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class BombThrownServerHandler : IServerHandler<BombThrownEvent>
{
    public BombThrownEvent Handle(BombThrownEvent networkEvent) => networkEvent;
}
