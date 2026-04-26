
using System;
using Multipeglin.Events.Network.Enemy;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;

namespace Multipeglin.Events.Handlers.Enemy;
public sealed class EnemyKilledClientHandler : IClientHandler<EnemyKilledEvent>
{
    public void Handle(EnemyKilledEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating)
            {
                return;
            }

            var enemyId = MultiplayerPlugin.Services.Resolve<EnemyIdentifier>();
            var enemy = enemyId.Find(e.EnemyId);
            if (enemy != null)
            {
                MultiplayerPlugin.Logger?.LogInfo($"[EnemyKilled] '{enemy.locKey}' (guid={e.EnemyId}) hp → 0");
                enemy.CurrentHealth = 0;
                // Trigger death animation by invoking UpdateHealthBar
                try
                {
                    var method = HarmonyLib.AccessTools.Method(typeof(global::Battle.Enemies.Enemy), "UpdateHealthBar");
                    method?.Invoke(enemy, null);
                }
                catch { }
                // Don't unregister GUID here — let the state sync handle cleanup
                // Don't invoke OnEnemyKilled — it has side effects that kill wrong enemies
                // when multiple enemies share the same locKey
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"EnemyKilled handler failed: {ex.Message}");
        }
    }
}
