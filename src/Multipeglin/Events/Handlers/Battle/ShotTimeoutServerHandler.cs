namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class ShotTimeoutServerHandler : IServerHandler<ShotTimeoutEvent>
{
    public ShotTimeoutEvent Handle(ShotTimeoutEvent networkEvent) => networkEvent;
}
