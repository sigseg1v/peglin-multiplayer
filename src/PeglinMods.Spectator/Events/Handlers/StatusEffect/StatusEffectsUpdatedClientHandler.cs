namespace PeglinMods.Spectator.Events.Handlers.StatusEffect;

using PeglinMods.Spectator.Events.Network.StatusEffect;

public sealed class StatusEffectsUpdatedClientHandler : IClientHandler<StatusEffectsUpdatedEvent>
{
    public void Handle(StatusEffectsUpdatedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Status effects updated ({networkEvent.Effects?.Count ?? 0} effects)");
    }
}
