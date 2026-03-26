namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class TurnCompleteClientHandler : IClientHandler<TurnCompleteEvent>
{
    public void Handle(TurnCompleteEvent networkEvent)
    {
        try
        {
            BattleController.OnTurnComplete?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"TurnComplete handler failed: {e.Message}");
        }
    }
}
