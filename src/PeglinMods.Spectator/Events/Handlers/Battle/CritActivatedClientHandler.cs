namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class CritActivatedClientHandler : IClientHandler<CritActivatedEvent>
{
    public void Handle(CritActivatedEvent networkEvent)
    {
        try
        {
            BattleController.onCriticalHitActivated?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"CritActivated handler failed: {e.Message}");
        }
    }
}
