using BepInEx.Logging;
using Map;
using PeglinMods.Spectator.Events.Network.Map;
using PeglinMods.Spectator.Spectator;

namespace PeglinMods.Spectator.Events.Subscriptions;

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
        SpectatorPlugin.Services?.TryResolve<ISpectatorMode>(out var mode) == true && mode.IsHosting;

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
        if (!IsHosting) return;
        _registry.Dispatch(new NodeSelectedEvent
        {
            MapName = mapName,
            Floor = floor,
            CruciballLevel = cruciballLevel
        });
    }
}
