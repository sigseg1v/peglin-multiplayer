using Multipeglin.Events.Network.Map;

namespace Multipeglin.Events.Handlers.Map;

public sealed class NodeSelectedServerHandler : IServerHandler<NodeSelectedEvent>
{
    public NodeSelectedEvent Handle(NodeSelectedEvent networkEvent) => networkEvent;
}
