namespace PeglinMods.Spectator.Events.Handlers.Map;

using PeglinMods.Spectator.Events.Network.Map;

public sealed class NodeSelectedClientHandler : IClientHandler<NodeSelectedEvent>
{
    public void Handle(NodeSelectedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Map node selected - {networkEvent.MapName} floor {networkEvent.Floor} (cruciball {networkEvent.CruciballLevel})");
    }
}
