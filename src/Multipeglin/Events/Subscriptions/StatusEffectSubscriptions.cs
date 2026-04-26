using Battle.StatusEffects;
using Multipeglin.Events.Network.StatusEffect;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Subscriptions;

public sealed class StatusEffectEventSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly IMultiplayerMode _multiplayerMode;

    public StatusEffectEventSubscriptions(IGameEventRegistry registry, IMultiplayerMode multiplayerMode)
    {
        _registry = registry;
        _multiplayerMode = multiplayerMode;
    }

    public void Subscribe()
    {
        PlayerStatusEffectController.OnPlayerStatusEffectsUpdated += OnStatusUpdated;
    }

    public void Unsubscribe()
    {
        PlayerStatusEffectController.OnPlayerStatusEffectsUpdated -= OnStatusUpdated;
    }

    private void OnStatusUpdated()
    {
        if (!_multiplayerMode.IsHosting)
            return;
        _registry.Dispatch(new StatusEffectsUpdatedEvent());
    }
}
