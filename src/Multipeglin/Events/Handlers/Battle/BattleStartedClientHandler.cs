
using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;
using Multipeglin.Utility;

namespace Multipeglin.Events.Handlers.Battle;
public sealed class BattleStartedClientHandler : IClientHandler<BattleStartedEvent>
{
    public void Handle(BattleStartedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo("[BattleStarted] Clearing enemy GUID registry for new battle");
            var enemyId = MultiplayerPlugin.Services?.TryResolve<EnemyIdentifier>(out var eid) == true ? eid : null;
            enemyId?.Clear();

            GameState.Appliers.MapStateApplier.ResetNavigationState();

            BattleController.OnBattleStarted?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BattleStarted handler failed: {e.Message}");
        }
    }
}
