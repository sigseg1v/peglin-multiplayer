using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

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
            MultiplayerPlugin.Logger.LogWarning($"CritActivated handler failed: {e.Message}");
        }
    }
}
