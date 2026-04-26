
using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;
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
