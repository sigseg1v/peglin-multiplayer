using Battle;
using Battle.Enemies;
using BepInEx.Logging;
using PeglinMods.Multiplayer.Events.Network.Enemy;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Utility;

namespace PeglinMods.Multiplayer.Events.Subscriptions;

public sealed class EnemySubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly EnemyIdentifier _enemyIdentifier;
    private readonly ManualLogSource _log;

    public EnemySubscriptions(IGameEventRegistry registry, EnemyIdentifier enemyIdentifier, ManualLogSource log)
    {
        _registry = registry;
        _enemyIdentifier = enemyIdentifier;
        _log = log;
    }

    private static bool IsHosting =>
        MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting;

    public void Subscribe()
    {
        Enemy.OnEnemySpawned += OnEnemySpawned;
        Enemy.OnEnemyDamaged += OnEnemyDamaged;
        Enemy.OnEnemyDestroyed += OnEnemyDestroyed;
        Enemy.OnEnemyKilled += OnEnemyKilled;
        Enemy.OnEnemyAttack += OnEnemyAttack;
        _log.LogInfo("EnemySubscriptions registered");
    }

    public void Unsubscribe()
    {
        Enemy.OnEnemySpawned -= OnEnemySpawned;
        Enemy.OnEnemyDamaged -= OnEnemyDamaged;
        Enemy.OnEnemyDestroyed -= OnEnemyDestroyed;
        Enemy.OnEnemyKilled -= OnEnemyKilled;
        Enemy.OnEnemyAttack -= OnEnemyAttack;
    }

    private void OnEnemySpawned(Enemy enemy)
    {
        if (!IsHosting) return;
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
    }

    private void OnEnemyDamaged(Enemy enemy, long damage, Enemy.EnemyDamageSource source)
    {
        if (!IsHosting) return;
        _registry.Dispatch(new EnemyDamagedEvent
        {
            EnemyId = _enemyIdentifier.GetId(enemy),
            Damage = damage,
            DamageSource = (int)source,
            RemainingHealth = enemy.CurrentHealth
        });
    }

    private void OnEnemyDestroyed(Enemy enemy)
    {
        if (!IsHosting) return;
        _registry.Dispatch(new EnemyDestroyedEvent
        {
            EnemyId = _enemyIdentifier.GetId(enemy)
        });
    }

    private void OnEnemyKilled(string locKey)
    {
        if (!IsHosting) return;

        // OnEnemyKilled only provides locKey, not the Enemy object.
        // Try to find the GUID by scanning enemies with this locKey that are dead/dying.
        string guid = "";
        var enemies = UnityEngine.Object.FindObjectsOfType<Enemy>();
        foreach (var e in enemies)
        {
            if (e != null && e.locKey == locKey && e.CurrentHealth <= 0)
            {
                guid = _enemyIdentifier.GetOrAssignGuid(e);
                break;
            }
        }

        _log.LogInfo($"[EnemySub] EnemyKilled: loc={locKey} guid={guid}");
        _registry.Dispatch(new EnemyKilledEvent
        {
            EnemyId = guid,
            LocKey = locKey
        });
    }

    private void OnEnemyAttack(float damage, bool melee, Enemy enemy, PlayerHealthController.DamageSource source, bool forceMaxHPDamage)
    {
        if (!IsHosting) return;
        _registry.Dispatch(new EnemyAttackEvent
        {
            EnemyId = _enemyIdentifier.GetId(enemy),
            Damage = damage,
            IsMelee = melee,
            DamageSource = (int)source
        });
    }
}
