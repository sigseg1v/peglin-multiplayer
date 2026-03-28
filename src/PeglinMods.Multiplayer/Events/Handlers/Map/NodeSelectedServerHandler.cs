namespace PeglinMods.Multiplayer.Events.Handlers.Map;

using PeglinMods.Multiplayer.Events.Network.Map;

public sealed class NodeSelectedServerHandler : IServerHandler<NodeSelectedEvent>
{
    public NodeSelectedEvent Handle(NodeSelectedEvent networkEvent) => networkEvent;
}
