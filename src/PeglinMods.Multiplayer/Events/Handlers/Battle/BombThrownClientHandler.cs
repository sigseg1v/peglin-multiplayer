namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

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
            MultiplayerPlugin.Logger.LogWarning($"BombThrown handler failed: {e.Message}");
        }
    }
}
