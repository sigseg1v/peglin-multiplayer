namespace PeglinMods.Spectator.Events.Handlers.Map;

using System;
using PeglinMods.Spectator.Events.Network.Map;

public sealed class NodeSelectedClientHandler : IClientHandler<NodeSelectedEvent>
{
    public void Handle(NodeSelectedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Map node selected - {networkEvent.MapName} floor {networkEvent.Floor} (cruciball {networkEvent.CruciballLevel})");
            // MapController.OnNodeSelectionEvent is a public static NodeSelectionEvent(string, int, int) delegate
            global::Map.MapController.OnNodeSelectionEvent?.Invoke(networkEvent.MapName, networkEvent.Floor, networkEvent.CruciballLevel);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"NodeSelected handler failed: {e.Message}");
        }
    }
}
