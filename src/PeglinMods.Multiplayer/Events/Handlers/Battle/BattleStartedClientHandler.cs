namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;
using PeglinMods.Multiplayer.Utility;

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
