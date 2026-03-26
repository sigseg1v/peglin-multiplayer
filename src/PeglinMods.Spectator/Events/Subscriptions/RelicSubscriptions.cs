using BepInEx.Logging;
using PeglinMods.Spectator.Events.Network.Relic;
using Relics;

namespace PeglinMods.Spectator.Events.Subscriptions;

public class RelicSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    private RelicManager.RelicManagement _onRelicAdded;
    private RelicManager.RelicManagement _onRelicRemoved;
    private RelicManager.RelicManagement _onRelicUsed;

    public RelicSubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    public void Subscribe()
    {
        _onRelicAdded = (Relic relic) =>
        {
            _registry.Dispatch(new RelicAddedEvent
            {
                RelicEffect = (int)relic.effect,
                RelicName = relic.locKey
            });
        };
        RelicManager.OnRelicAdded += _onRelicAdded;

        _onRelicRemoved = (Relic relic) =>
        {
            _registry.Dispatch(new RelicRemovedEvent
            {
                RelicEffect = (int)relic.effect
            });
        };
        RelicManager.OnRelicRemoved += _onRelicRemoved;

        _onRelicUsed = (Relic relic) =>
        {
            _registry.Dispatch(new RelicUsedEvent
            {
                RelicEffect = (int)relic.effect
            });
        };
        RelicManager.OnRelicUsed += _onRelicUsed;

        _log.LogInfo("RelicSubscriptions registered");
    }

    public void Unsubscribe()
    {
        RelicManager.OnRelicAdded -= _onRelicAdded;
        RelicManager.OnRelicRemoved -= _onRelicRemoved;
        RelicManager.OnRelicUsed -= _onRelicUsed;
    }
}
