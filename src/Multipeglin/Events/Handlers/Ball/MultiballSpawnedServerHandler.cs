namespace Multipeglin.Events.Handlers.Ball;

using Multipeglin.Events.Network.Ball;

public sealed class MultiballSpawnedServerHandler : IServerHandler<MultiballSpawnedEvent>
{
    public MultiballSpawnedEvent Handle(MultiballSpawnedEvent networkEvent) => networkEvent;
}
