namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

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
            MultiplayerPlugin.Logger.LogWarning($"TurnComplete handler failed: {e.Message}");
        }
    }
}
