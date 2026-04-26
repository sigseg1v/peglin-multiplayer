using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;
using Multipeglin.Utility;

namespace Multipeglin.Events.Handlers.Battle;

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
