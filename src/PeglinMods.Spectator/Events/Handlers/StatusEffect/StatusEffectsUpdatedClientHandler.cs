namespace PeglinMods.Spectator.Events.Handlers.StatusEffect;

using System;
using global::Battle.StatusEffects;
using PeglinMods.Spectator.Events.Network.StatusEffect;

public sealed class StatusEffectsUpdatedClientHandler : IClientHandler<StatusEffectsUpdatedEvent>
{
    public void Handle(StatusEffectsUpdatedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Status effects updated ({networkEvent.Effects?.Count ?? 0} effects)");
            // PlayerStatusEffectController.OnPlayerStatusEffectsUpdated is a public static delegate
            PlayerStatusEffectController.OnPlayerStatusEffectsUpdated?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"StatusEffectsUpdated handler failed: {e.Message}");
        }
    }
}
