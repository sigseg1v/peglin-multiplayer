namespace Multipeglin.Events.Handlers.Ball;

using Multipeglin.Events.Network.Ball;

public sealed class ShotFiredServerHandler : IServerHandler<ShotFiredEvent>
{
    public ShotFiredEvent Handle(ShotFiredEvent networkEvent) => networkEvent;
}
