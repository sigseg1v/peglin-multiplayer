using Battle.Enemies;
using BepInEx.Logging;
using PeglinMods.Spectator.Events.Network.Enemy;
using PeglinMods.Spectator.Utility;

namespace PeglinMods.Spectator.Events.Subscriptions;

public class EnemySubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly EnemyIdentifier _enemyIdentifier;
    private readonly ManualLogSource _log;

    private Enemy.EnemySpawned _onEnemySpawned;
    private Enemy.EnemyDamaged _onEnemyDamaged;
    private Enemy.EnemyDestroyed _onEnemyDestroyed;
    private Enemy.EnemyDataEvent _onEnemyKilled;
    private Enemy.EnemyAttacked _onEnemyAttack;
    private EnemyManager.EnemyMoved _onEnemyMoved;

    public EnemySubscriptions(IGameEventRegistry registry, EnemyIdentifier enemyIdentifier, ManualLogSource log)
    {
        _registry = registry;
        _enemyIdentifier = enemyIdentifier;
        _log = log;
    }

    public void Subscribe()
    {
        _onEnemySpawned = (Enemy enemy) =>
        {
            _registry.Dispatch(new EnemySpawnedEvent
            {
                EnemyId = _enemyIdentifier.GetId(enemy),
                LocKey = enemy.locKey,
                CurrentHealth = enemy.CurrentHealth,
                MaxHealth = enemy.maxHealth,
                SlotIndex = -1,
                MeleeDamage = enemy.DamagePerMeleeAttack,
                RangedDamage = enemy.DamagePerRangedAttack
            });
        };
        Enemy.OnEnemySpawned += _onEnemySpawned;

        _onEnemyDamaged = (Enemy enemy, long damage, Enemy.EnemyDamageSource source) =>
        {
            _registry.Dispatch(new EnemyDamagedEvent
            {
                EnemyId = _enemyIdentifier.GetId(enemy),
                Damage = damage,
                DamageSource = (int)source,
                RemainingHealth = enemy.CurrentHealth
            });
        };
        Enemy.OnEnemyDamaged += _onEnemyDamaged;

        _onEnemyDestroyed = (Enemy enemy) =>
        {
            _registry.Dispatch(new EnemyDestroyedEvent
            {
                EnemyId = _enemyIdentifier.GetId(enemy)
            });
        };
        Enemy.OnEnemyDestroyed += _onEnemyDestroyed;

        _onEnemyKilled = (string locKey) =>
        {
            _registry.Dispatch(new EnemyKilledEvent
            {
                EnemyId = "",
                LocKey = locKey
            });
        };
        Enemy.OnEnemyKilled += _onEnemyKilled;

        _onEnemyAttack = (float damage, bool melee, Enemy enemy, Battle.PlayerHealthController.DamageSource source, bool forceMaxHPDamage) =>
        {
            _registry.Dispatch(new EnemyAttackEvent
            {
                EnemyId = _enemyIdentifier.GetId(enemy),
                Damage = damage,
                IsMelee = melee,
                DamageSource = (int)source
            });
        };
        Enemy.OnEnemyAttack += _onEnemyAttack;

        _onEnemyMoved = (int fromSlot, int toSlot, Enemy enemy) =>
        {
            _registry.Dispatch(new EnemyMovedEvent
            {
                EnemyId = _enemyIdentifier.GetId(enemy),
                FromSlot = fromSlot,
                ToSlot = toSlot
            });
        };
        EnemyManager.OnEnemyMoved += _onEnemyMoved;

        _log.LogInfo("EnemySubscriptions registered");
    }

    public void Unsubscribe()
    {
        Enemy.OnEnemySpawned -= _onEnemySpawned;
        Enemy.OnEnemyDamaged -= _onEnemyDamaged;
        Enemy.OnEnemyDestroyed -= _onEnemyDestroyed;
        Enemy.OnEnemyKilled -= _onEnemyKilled;
        Enemy.OnEnemyAttack -= _onEnemyAttack;
        EnemyManager.OnEnemyMoved -= _onEnemyMoved;
    }
}
