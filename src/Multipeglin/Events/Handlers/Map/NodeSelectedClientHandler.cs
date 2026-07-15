using System;
using Multipeglin.Events.Network.Map;
using UnityEngine.SceneManagement;

namespace Multipeglin.Events.Handlers.Map;

public sealed class NodeSelectedClientHandler : IClientHandler<NodeSelectedEvent>
{
    public void Handle(NodeSelectedEvent networkEvent)
    {
        try
        {
            var clientScene = SceneManager.GetActiveScene().name;
            // Do not invoke MapController.OnNodeSelectionEvent on the client — it starts
            // MapController's NodeSelected camera-pan coroutine (DontDestroyOnLoad) which
            // leaks into PegMinigame and pushes the pegboard off-screen. MapStateApplier
            // already syncs floor count and player position from host snapshots.
            MultiplayerPlugin.Logger.LogInfo(
                $"Multiplayer: Map node selected - {networkEvent.MapName} floor {networkEvent.Floor} " +
                $"(cruciball {networkEvent.CruciballLevel}) on '{clientScene}' (position synced via MapApplier)");
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"NodeSelected handler failed: {e.Message}");
        }
    }
}
