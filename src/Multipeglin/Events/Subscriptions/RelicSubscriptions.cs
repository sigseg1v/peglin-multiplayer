using BepInEx.Logging;
using Multipeglin.Events.Network.Relic;
using Multipeglin.Multiplayer;
using Relics;

namespace Multipeglin.Events.Subscriptions;

public sealed class RelicSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    public RelicSubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    private static bool IsHosting =>
        MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting;

    public void Subscribe()
    {
        RelicManager.OnRelicAdded += OnRelicAdded;
        RelicManager.OnRelicRemoved += OnRelicRemoved;
        // commented out for performance: per-peg-hit relics drove this handler
        // thousands of times per shot, dispatching/serializing/sending events
        // whose only consumer was a client log line.
        // RelicManager.OnRelicUsed += OnRelicUsed;
        _log.LogInfo("RelicSubscriptions registered");
    }

    public void Unsubscribe()
    {
        RelicManager.OnRelicAdded -= OnRelicAdded;
        RelicManager.OnRelicRemoved -= OnRelicRemoved;
        // commented out for performance: matches the disabled Subscribe hookup above.
        // RelicManager.OnRelicUsed -= OnRelicUsed;
    }

    private void OnRelicAdded(Relic relic)
    {
        if (!IsHosting)
        {
            return;
        }

        _registry.Dispatch(new RelicAddedEvent
        {
            RelicEffect = (int)relic.effect,
            RelicName = relic.locKey
        });
    }

    private void OnRelicRemoved(Relic relic)
    {
        if (!IsHosting)
        {
            return;
        }

        _registry.Dispatch(new RelicRemovedEvent { RelicEffect = (int)relic.effect });
    }

    // commented out for performance: per-peg-hit relics drove this through
    // Dispatch -> serialize -> network send thousands of times per shot, with
    // no consumer beyond a client log line.
    /*
    private void OnRelicUsed(Relic relic)
    {
        if (!IsHosting)
        {
            return;
        }

        _registry.Dispatch(new RelicUsedEvent { RelicEffect = (int)relic.effect });
    }
    */
}
