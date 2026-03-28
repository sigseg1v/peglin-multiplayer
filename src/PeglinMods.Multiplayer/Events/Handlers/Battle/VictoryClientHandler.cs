namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

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
            MultiplayerPlugin.Logger.LogWarning($"Victory handler failed: {e.Message}");
        }
    }
}
