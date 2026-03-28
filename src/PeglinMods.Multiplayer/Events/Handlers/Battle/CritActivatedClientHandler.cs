namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

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
