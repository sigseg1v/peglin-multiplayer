namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using System;
using PeglinMods.Multiplayer.Events.Network.Enemy;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Utility;

public sealed class EnemyAttackClientHandler : IClientHandler<EnemyAttackEvent>
{
    public void Handle(EnemyAttackEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            var enemyId = MultiplayerPlugin.Services.Resolve<EnemyIdentifier>();
            var enemy = enemyId.Find(e.EnemyId);
            if (enemy != null)
            {
                // Invoke the attack delegate — triggers attack animation, damage, health bar
                global::Battle.Enemies.Enemy.OnEnemyAttack?.Invoke(e.Damage, e.IsMelee, enemy);
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"EnemyAttack handler failed: {ex.Message}");
        }
    }
}
