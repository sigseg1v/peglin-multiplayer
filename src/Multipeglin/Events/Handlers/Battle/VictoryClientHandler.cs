namespace Multipeglin.Events.Handlers.Battle;

using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;
using Multipeglin.GameState.Appliers;
using Multipeglin.Multiplayer;

public sealed class VictoryClientHandler : IClientHandler<VictoryEvent>
{
    public void Handle(VictoryEvent networkEvent)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode != null && mode.IsSpectating)
            {
                MapStateApplier.ClientWaitingMessage = "Waiting for other players...";
            }

            BattleController.OnVictory?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"Victory handler failed: {e.Message}");
        }
    }
}
