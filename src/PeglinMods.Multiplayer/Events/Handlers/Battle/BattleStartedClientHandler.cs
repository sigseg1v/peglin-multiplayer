namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class BattleStartedClientHandler : IClientHandler<BattleStartedEvent>
{
    public void Handle(BattleStartedEvent networkEvent)
    {
        try
        {
            BattleController.OnBattleStarted?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BattleStarted handler failed: {e.Message}");
        }
    }
}
