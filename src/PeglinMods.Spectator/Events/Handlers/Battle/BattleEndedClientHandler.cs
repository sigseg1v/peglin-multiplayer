namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class BattleEndedClientHandler : IClientHandler<BattleEndedEvent>
{
    public void Handle(BattleEndedEvent networkEvent)
    {
        try
        {
            BattleController.OnBattleEnded?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"BattleEnded handler failed: {e.Message}");
        }
    }
}
