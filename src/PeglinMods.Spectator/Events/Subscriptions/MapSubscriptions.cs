using BepInEx.Logging;
using Map;
using PeglinMods.Spectator.Events.Network.Map;

namespace PeglinMods.Spectator.Events.Subscriptions;

public class MapSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    private MapController.NodeSelectionEvent _onNodeSelected;

    public MapSubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    public void Subscribe()
    {
        _onNodeSelected = (string mapName, int floor, int cruciballLevel) =>
        {
            _registry.Dispatch(new NodeSelectedEvent
            {
                MapName = mapName,
                Floor = floor,
                CruciballLevel = cruciballLevel
            });
        };
        MapController.OnNodeSelectionEvent += _onNodeSelected;

        _log.LogInfo("MapSubscriptions registered");
    }

    public void Unsubscribe()
    {
        MapController.OnNodeSelectionEvent -= _onNodeSelected;
    }
}
