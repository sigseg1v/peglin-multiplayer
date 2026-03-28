namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class BombDetonatedClientHandler : IClientHandler<BombDetonatedEvent>
{
    public void Handle(BombDetonatedEvent networkEvent)
    {
        try
        {
            BattleController.OnBombDetonated?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BombDetonated handler failed: {e.Message}");
        }
    }
}
