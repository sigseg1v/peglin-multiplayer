using System;
using Map;
using PeglinMods.Multiplayer.Events.Network.Map;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;
using Worldmap;

namespace PeglinMods.Multiplayer.Events.Handlers.Map;

public sealed class NodeActivatedClientHandler : IClientHandler<NodeActivatedEvent>
{
    public void Handle(NodeActivatedEvent e)
    {
        var log = MultiplayerPlugin.Logger;
        try
        {
            var mode = MultiplayerPlugin.Services?.Resolve<IMultiplayerMode>();
            if (mode == null || !mode.IsSpectating) return;

            var targetPos = new Vector2(e.PosX, e.PosY);
            var nodes = UnityEngine.Object.FindObjectsOfType<MapNode>();
            MapNode closest = null;
            float minDist = float.MaxValue;

            foreach (var node in nodes)
            {
                float dist = Vector2.Distance(
                    new Vector2(node.transform.position.x, node.transform.position.y),
                    targetPos);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = node;
                }
            }

            if (closest == null || minDist > 2f)
            {
                log?.LogWarning($"[NodeActivated] No matching node found near ({e.PosX:F1}, {e.PosY:F1}), found {nodes.Length} nodes");
                return;
            }

            log?.LogInfo($"[NodeActivated] Found node at dist={minDist:F2}, activating...");

            // Generate map data for this node if not already done
            closest.GenerateMapData();

            // Activate the node — this calls MapController.ResolveNode which sets
            // StaticGameData.dataToLoad and eventually loads the Battle scene
            closest.ActivateNode();
        }
        catch (Exception ex)
        {
            log?.LogError($"[NodeActivated] Failed: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
