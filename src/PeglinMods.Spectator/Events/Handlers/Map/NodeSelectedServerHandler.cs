namespace PeglinMods.Spectator.Events.Handlers.Map;

using PeglinMods.Spectator.Events.Network.Map;

public sealed class NodeSelectedServerHandler : IServerHandler<NodeSelectedEvent>
{
    public NodeSelectedEvent Handle(NodeSelectedEvent networkEvent) => networkEvent;
}
