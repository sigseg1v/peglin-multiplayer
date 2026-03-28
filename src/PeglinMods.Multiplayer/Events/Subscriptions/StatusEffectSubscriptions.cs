using Battle.StatusEffects;
using PeglinMods.Multiplayer.Events.Network.StatusEffect;
using PeglinMods.Multiplayer.Multiplayer;

namespace PeglinMods.Multiplayer.Events.Subscriptions;

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
        if (!_multiplayerMode.IsHosting) return;
        _registry.Dispatch(new StatusEffectsUpdatedEvent());
    }
}
