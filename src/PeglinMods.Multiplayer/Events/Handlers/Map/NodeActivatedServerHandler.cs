using PeglinMods.Multiplayer.Events.Network.Map;

namespace PeglinMods.Multiplayer.Events.Handlers.Map;

public sealed class NodeActivatedServerHandler : IServerHandler<NodeActivatedEvent>
{
    public NodeActivatedEvent Handle(NodeActivatedEvent e) => e;
}
