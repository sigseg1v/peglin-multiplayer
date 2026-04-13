namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;
using PeglinMods.Multiplayer.GameState.Appliers;
using PeglinMods.Multiplayer.Multiplayer;

public sealed class VictoryClientHandler : IClientHandler<VictoryEvent>
{
    public void Handle(VictoryEvent networkEvent)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode != null && mode.IsSpectating)
            {
                MapStateApplier.ClientWaitingMessage = "Waiting for host...";
            }

            BattleController.OnVictory?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"Victory handler failed: {e.Message}");
        }
    }
}
