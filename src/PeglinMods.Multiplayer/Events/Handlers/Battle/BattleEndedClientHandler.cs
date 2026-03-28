namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class BattleEndedClientHandler : IClientHandler<BattleEndedEvent>
{
    public void Handle(BattleEndedEvent networkEvent)
    {
        try
        {
            BattleController.OnBattleEnded?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BattleEnded handler failed: {e.Message}");
        }
    }
}
