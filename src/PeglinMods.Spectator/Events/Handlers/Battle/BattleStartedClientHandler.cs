namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class BattleStartedClientHandler : IClientHandler<BattleStartedEvent>
{
    public void Handle(BattleStartedEvent networkEvent)
    {
        try
        {
            BattleController.OnBattleStarted?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"BattleStarted handler failed: {e.Message}");
        }
    }
}
