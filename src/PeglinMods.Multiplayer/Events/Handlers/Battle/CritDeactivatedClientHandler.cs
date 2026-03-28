namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

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
            MultiplayerPlugin.Logger.LogWarning($"CritDeactivated handler failed: {e.Message}");
        }
    }
}
