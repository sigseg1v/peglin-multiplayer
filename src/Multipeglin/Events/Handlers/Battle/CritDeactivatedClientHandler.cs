namespace Multipeglin.Events.Handlers.Battle;

using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;

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
