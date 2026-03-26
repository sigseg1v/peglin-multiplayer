namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class BombThrownClientHandler : IClientHandler<BombThrownEvent>
{
    public void Handle(BombThrownEvent networkEvent)
    {
        try
        {
            BattleController.OnBombThrown?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"BombThrown handler failed: {e.Message}");
        }
    }
}
