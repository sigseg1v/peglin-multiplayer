namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class VictoryClientHandler : IClientHandler<VictoryEvent>
{
    public void Handle(VictoryEvent networkEvent)
    {
        try
        {
            BattleController.OnVictory?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"Victory handler failed: {e.Message}");
        }
    }
}
