using Multipeglin.Events.Network.Ball;

namespace Multipeglin.Events.Handlers.Ball;

public sealed class ShotFiredServerHandler : IServerHandler<ShotFiredEvent>
{
    public ShotFiredEvent Handle(ShotFiredEvent networkEvent) => networkEvent;
}
