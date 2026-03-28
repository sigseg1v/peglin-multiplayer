namespace PeglinMods.Multiplayer.Events.Handlers.Map;

using System;
using PeglinMods.Multiplayer.Events.Network.Map;

public sealed class NodeSelectedClientHandler : IClientHandler<NodeSelectedEvent>
{
    public void Handle(NodeSelectedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Map node selected - {networkEvent.MapName} floor {networkEvent.Floor} (cruciball {networkEvent.CruciballLevel})");
            // MapController.OnNodeSelectionEvent is a public static NodeSelectionEvent(string, int, int) delegate
            global::Map.MapController.OnNodeSelectionEvent?.Invoke(networkEvent.MapName, networkEvent.Floor, networkEvent.CruciballLevel);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"NodeSelected handler failed: {e.Message}");
        }
    }
}
