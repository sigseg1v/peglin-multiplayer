namespace Multipeglin.Events.Handlers.Enemy;

using System;
using global::Battle.Enemies;
using Multipeglin.Events.Network.Enemy;
using Multipeglin.Utility;

public sealed class EnemyDamagedClientHandler : IClientHandler<EnemyDamagedEvent>
{
    // Track which missing GUIDs we've already warned about so per-frame DOTs (relic
    // effect 50, etc.) don't dump the registry hundreds of times per second.
    private static readonly System.Collections.Generic.HashSet<string> _warnedMissing = new System.Collections.Generic.HashSet<string>();

    public void Handle(EnemyDamagedEvent networkEvent)
    {
        try
        {
            var enemyIdentifier = MultiplayerPlugin.Services.Resolve<EnemyIdentifier>();
            var enemy = enemyIdentifier.Find(networkEvent.EnemyId);
            if (enemy != null)
            {
                enemy.CurrentHealth = networkEvent.RemainingHealth;

                global::Battle.Enemies.Enemy.OnEnemyDamaged?.Invoke(
                    enemy,
                    networkEvent.Damage,
                    (global::Battle.Enemies.Enemy.EnemyDamageSource)networkEvent.DamageSource);
            }
            else if (_warnedMissing.Add(networkEvent.EnemyId))
            {
                MultiplayerPlugin.Logger.LogWarning($"[EnemyDamaged] Could not find enemy guid={networkEvent.EnemyId} (will not warn again for this guid)");
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyDamaged handler failed: {e.Message}");
        }
    }
}
