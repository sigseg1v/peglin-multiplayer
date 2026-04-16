namespace Multipeglin.Events.Handlers.Map;

using Multipeglin.Events.Network.Map;

public sealed class NodeSelectedServerHandler : IServerHandler<NodeSelectedEvent>
{
    public NodeSelectedEvent Handle(NodeSelectedEvent networkEvent) => networkEvent;
}
