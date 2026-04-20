namespace Multipeglin.Events.Handlers.Ball;

using Multipeglin.Events.Network.Ball;

public sealed class MultiballPositionServerHandler : IServerHandler<MultiballPositionEvent>
{
    public MultiballPositionEvent Handle(MultiballPositionEvent networkEvent) => networkEvent;
}
