using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

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
