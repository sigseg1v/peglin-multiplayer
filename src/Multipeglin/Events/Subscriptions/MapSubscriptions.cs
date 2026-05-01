using BepInEx.Logging;
using Map;
using Multipeglin.Events.Network.Map;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Subscriptions;

public sealed class MapSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    public MapSubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    private static bool IsHosting =>
        MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting;

    public void Subscribe()
    {
        MapController.OnNodeSelectionEvent += OnNodeSelected;
        _log.LogInfo("MapSubscriptions registered");
    }

    public void Unsubscribe()
    {
        MapController.OnNodeSelectionEvent -= OnNodeSelected;
    }

    private void OnNodeSelected(string mapName, int floor, int cruciballLevel)
    {
        if (!IsHosting)
        {
            return;
        }

        // Wrapped in try/catch because this is invoked from inside MapController's
        // NodeSelected coroutine (line 1052 in decomp). If Dispatch throws, the
        // exception propagates up the iterator and kills the coroutine before it
        // can reach DoNodeSelectionFadeOut → LoadSceneFromMapData — softlocking
        // the host on the map screen with no visible error.
        try
        {
            _registry.Dispatch(new NodeSelectedEvent
            {
                MapName = mapName,
                Floor = floor,
                CruciballLevel = cruciballLevel
            });
        }
        catch (System.Exception ex)
        {
            _log.LogWarning($"MapSubscriptions: NodeSelectedEvent dispatch threw: {ex}");
        }
    }
}
