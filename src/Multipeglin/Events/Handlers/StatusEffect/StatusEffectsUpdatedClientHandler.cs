namespace Multipeglin.Events.Handlers.StatusEffect;

using System;
using global::Battle.StatusEffects;
using Multipeglin.Events.Network.StatusEffect;

public sealed class StatusEffectsUpdatedClientHandler : IClientHandler<StatusEffectsUpdatedEvent>
{
    public void Handle(StatusEffectsUpdatedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Status effects updated ({networkEvent.Effects?.Count ?? 0} effects)");
            // PlayerStatusEffectController.OnPlayerStatusEffectsUpdated is a public static delegate
            PlayerStatusEffectController.OnPlayerStatusEffectsUpdated?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"StatusEffectsUpdated handler failed: {e.Message}");
        }
    }
}
