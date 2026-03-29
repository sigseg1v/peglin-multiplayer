namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;
using PeglinMods.Multiplayer.Utility;

public sealed class BattleEndedClientHandler : IClientHandler<BattleEndedEvent>
{
    public void Handle(BattleEndedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo("[BattleEnded] Clearing enemy GUID registry");
            var enemyId = MultiplayerPlugin.Services?.TryResolve<EnemyIdentifier>(out var eid) == true ? eid : null;
            enemyId?.Clear();

            BattleController.OnBattleEnded?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BattleEnded handler failed: {e.Message}");
        }
    }
}
