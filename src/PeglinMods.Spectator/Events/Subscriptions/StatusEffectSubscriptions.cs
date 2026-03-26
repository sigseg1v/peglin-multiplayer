using Battle.StatusEffects;
using PeglinMods.Spectator.Events.Network.StatusEffect;
using PeglinMods.Spectator.Spectator;

namespace PeglinMods.Spectator.Events.Subscriptions;

public sealed class StatusEffectEventSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ISpectatorMode _spectatorMode;

    public StatusEffectEventSubscriptions(IGameEventRegistry registry, ISpectatorMode spectatorMode)
    {
        _registry = registry;
        _spectatorMode = spectatorMode;
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
        if (!_spectatorMode.IsHosting) return;
        _registry.Dispatch(new StatusEffectsUpdatedEvent());
    }
}
