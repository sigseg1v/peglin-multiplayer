using Multipeglin.Events.Network.Map;

namespace Multipeglin.Events.Handlers.Map;

public sealed class NodeActivatedServerHandler : IServerHandler<NodeActivatedEvent>
{
    public NodeActivatedEvent Handle(NodeActivatedEvent e) => e;
}
