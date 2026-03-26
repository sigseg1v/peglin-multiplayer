namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

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
            SpectatorPlugin.Logger.LogWarning($"BombDetonated handler failed: {e.Message}");
        }
    }
}
