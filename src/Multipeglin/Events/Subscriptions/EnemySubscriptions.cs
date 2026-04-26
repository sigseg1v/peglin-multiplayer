using Battle;
using Battle.Enemies;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.Events.Network.Enemy;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.Events.Subscriptions;

public sealed class EnemySubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly EnemyIdentifier _enemyIdentifier;
    private readonly ManualLogSource _log;
    private readonly CoopStateManager _coopStateManager;

    public EnemySubscriptions(IGameEventRegistry registry, EnemyIdentifier enemyIdentifier, ManualLogSource log,
        CoopStateManager coopStateManager = null)
    {
        _registry = registry;
        _enemyIdentifier = enemyIdentifier;
        _log = log;
        _coopStateManager = coopStateManager;
    }

    private static bool IsHosting =>
        MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting;

    /// <summary>
    /// Slot index to credit DamageDealt to, overriding ActivePlayerSlot.
    /// BattleController_DoAttack_Prefix applies every player's damage sequentially on the
    /// host — but by then ActivePlayerSlot has already been swapped back to slot 0 for the
    /// next round, so OnEnemyDamaged would attribute all damage to the host. Setting this
    /// to the shot's SlotIndex around each enemy.Damage() call keeps the tally correct.
    /// -1 means "use ActivePlayerSlot".
    /// </summary>
    public static int DamageAttributionSlotOverride = -1;

    private int PlayerCount => _coopStateManager?.TotalPlayerCount ?? 1;

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
        if (!IsHosting)
            return;

        // Scale enemy stats by coop player count before capturing/dispatching, so
        // the client receives the already-scaled values through the heartbeat and
        // the host's MeleeAttack/RangedAttack coroutines pick up the scaled damage
        // fields when the enemy takes its turn.
        ApplyCoopScaling(enemy);

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

    /// <summary>
    /// Scale enemy HP based on the number of coop players.
    ///   1p: x1.0   2p: x1.75   3p: x2.25   4p: x2.5
    /// Damage is NOT scaled — high player counts were too punishing.
    /// Called from OnEnemySpawned (which fires inside Enemy.Initialize AFTER
    /// _maxHealth is set and BEFORE UpdateHealthBar), so the bar picks up the
    /// scaled values automatically on the host. Client HP is synced via the
    /// EnemyStateApplier heartbeat.
    /// </summary>
    private void ApplyCoopScaling(Enemy enemy)
    {
        int players = PlayerCount;
        float hpMult = players switch
        {
            <= 1 => 1f,
            2 => 1.75f,
            3 => 2.25f,
            _ => 2.5f,
        };
        if (hpMult == 1f)
            return;

        try
        {
            var maxHealthField = AccessTools.Field(typeof(Enemy), "_maxHealth");
            float oldMax = enemy.maxHealth;
            float newMax = oldMax * hpMult;
            maxHealthField?.SetValue(enemy, newMax);
            // Initialize has just done `CurrentHealth = maxHealth;` — keep them in sync.
            enemy.CurrentHealth = newMax;

            _log.LogInfo($"[EnemyScale] {enemy.name} players={players} hp {oldMax:F0}->{newMax:F0}");
        }
        catch (System.Exception ex)
        {
            _log.LogWarning($"[EnemyScale] failed to scale {enemy?.name}: {ex.Message}");
        }
    }

    private void OnEnemyDamaged(Enemy enemy, long damage, Enemy.EnemyDamageSource source)
    {
        if (!IsHosting)
            return;

        // Attribute damage to the active coop player's run-summary tally.
        // During DoAttack's manual per-player replay, DamageAttributionSlotOverride is set
        // to the shot's owner so each shot's damage is credited to the player who fired it.
        if (damage > 0 && _coopStateManager != null)
        {
            int attributionSlot = DamageAttributionSlotOverride >= 0
                ? DamageAttributionSlotOverride
                : _coopStateManager.ActivePlayerSlot;
            var slot = _coopStateManager.GetPlayerState(attributionSlot);
            if (slot != null)
                slot.DamageDealt += damage;
        }

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
        if (!IsHosting)
            return;
        _registry.Dispatch(new EnemyDestroyedEvent
        {
            EnemyId = _enemyIdentifier.GetId(enemy)
        });
    }

    private void OnEnemyKilled(string locKey)
    {
        if (!IsHosting)
            return;

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
        if (!IsHosting)
            return;
        _registry.Dispatch(new EnemyAttackEvent
        {
            EnemyId = _enemyIdentifier.GetId(enemy),
            Damage = damage,
            IsMelee = melee,
            DamageSource = (int)source
        });
    }
}
