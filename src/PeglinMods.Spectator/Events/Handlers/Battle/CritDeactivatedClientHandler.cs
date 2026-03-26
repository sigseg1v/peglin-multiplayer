namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class CritDeactivatedClientHandler : IClientHandler<CritDeactivatedEvent>
{
    public void Handle(CritDeactivatedEvent networkEvent)
    {
        try
        {
            BattleController.onCriticalHitDeactivated?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"CritDeactivated handler failed: {e.Message}");
        }
    }
}
