using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.GameState.Providers;

public class EnemyStateProvider : IGameStateProvider<EnemyStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly EnemyIdentifier _enemyId;

    public EnemyStateProvider(ManualLogSource log, EnemyIdentifier enemyId)
    {
        _log = log;
        _enemyId = enemyId;
    }

    public EnemyStateSnapshot Capture()
    {
        try
        {
            var snapshot = new EnemyStateSnapshot();

            // Battle state
            var bc = UnityEngine.Object.FindObjectOfType<Battle.BattleController>();
            if (bc != null)
            {
                var stateField = AccessTools.Field(typeof(Battle.BattleController), "_battleState");
                if (stateField != null)
                {
                    var state = stateField.GetValue(null); // static field
                    snapshot.BattleState = (int)state;
                    snapshot.BattleStateName = state.ToString();
                }

                // Ball spawn location — bosses like SlimeBoss alternate this each turn.
                // Sent so the client's aim ball appears at the same origin as the host.
                snapshot.BallSpawnX = bc.pachinkoBallSpawnLocation.x;
                snapshot.BallSpawnY = bc.pachinkoBallSpawnLocation.y;
            }

            // Enemies
            var em = UnityEngine.Object.FindObjectOfType<EnemyManager>();
            if (em != null)
            {
                var enemiesList = AccessTools.Field(typeof(EnemyManager), "Enemies")
                    ?.GetValue(em) as List<Battle.Enemies.Enemy>;

                if (enemiesList != null)
                {
                    for (int i = 0; i < enemiesList.Count; i++)
                    {
                        var enemy = enemiesList[i];
                        if (enemy == null) continue;

                        // Use EnemyIdentifier for stable GUID assignment
                        var guid = _enemyId.GetOrAssignGuid(enemy);

                        var entry = new EnemyEntry
                        {
                            Id = guid,
                            LocKey = enemy.locKey ?? enemy.gameObject.name,
                            EnemyName = enemy.gameObject.name,
                            CurrentHealth = enemy.CurrentHealth,
                            MeleeDamage = enemy.DamagePerMeleeAttack,
                            RangedDamage = enemy.DamagePerRangedAttack,
                            SlotIndex = i,
                            PosX = enemy.transform.position.x,
                            PosY = enemy.transform.position.y,
                            IsFlying = enemy.IsFlying,
                        };

                        // Max health (protected field)
                        var maxHpField = AccessTools.Field(typeof(Battle.Enemies.Enemy), "_maxHealth");
                        if (maxHpField != null)
                            entry.MaxHealth = (float)maxHpField.GetValue(enemy);

                        // Charge
                        entry.CurrentCharge = enemy.currentChargeTime;
                        var chargeLenField = AccessTools.Field(typeof(Battle.Enemies.Enemy), "AttackChargeLength");
                        if (chargeLenField != null)
                            entry.ChargeTime = (int)chargeLenField.GetValue(enemy);

                        // Status effects
                        try
                        {
                            var statusField = AccessTools.Field(typeof(Battle.Enemies.Enemy), "_statusEffects");
                            var effects = statusField?.GetValue(enemy) as System.Collections.IList;
                            if (effects != null)
                            {
                                entry.StatusEffects = new List<StatusEffectEntry>();
                                foreach (var eff in effects)
                                {
                                    var typeF = AccessTools.Field(eff.GetType(), "EffectType");
                                    var intF = AccessTools.Field(eff.GetType(), "Intensity");
                                    if (typeF != null)
                                    {
                                        var et = typeF.GetValue(eff);
                                        entry.StatusEffects.Add(new StatusEffectEntry
                                        {
                                            EffectType = (int)et,
                                            EffectName = et.ToString(),
                                            Intensity = (int)(intF?.GetValue(eff) ?? 0)
                                        });
                                    }
                                }
                            }
                        }
                        catch { }

                        snapshot.Enemies.Add(entry);
                        var effectStr = entry.StatusEffects?.Count > 0
                            ? string.Join(",", entry.StatusEffects.ConvertAll(e => $"{e.EffectName}={e.Intensity}"))
                            : "none";
                        _log.LogInfo($"[EnemyProvider] Captured enemy: guid={guid} loc={entry.LocKey} name={entry.EnemyName} " +
                            $"hp={entry.CurrentHealth}/{entry.MaxHealth} pos=({entry.PosX:F1},{entry.PosY:F1}) slot={i} effects=[{effectStr}]");
                    }
                }
            }

            // Capture upcoming enemy names from EnemyInfoManager._enemyInfoElements.
            // This is the VISUAL queue — only contains enemies not yet on the battlefield.
            // _upcomingSpawns contains ALL wave data including already-spawned entries.
            var eim = UnityEngine.Object.FindObjectOfType<Battle.EnemyInfoManager>();
            if (eim != null)
            {
                var elementsField = AccessTools.Field(typeof(Battle.EnemyInfoManager), "_enemyInfoElements");
                var elements = elementsField?.GetValue(eim) as System.Collections.Generic.Queue<Battle.EnemyInfoElement>;
                if (elements != null && elements.Count > 0)
                {
                    foreach (var element in elements)
                    {
                        if (element == null) continue;
                        // EnemyInfoElement has an _enemy field set by SetEnemy()
                        var enemyField = AccessTools.Field(typeof(Battle.EnemyInfoElement), "_enemy");
                        var enemy = enemyField?.GetValue(element) as Battle.Enemies.Enemy;
                        if (enemy != null)
                            snapshot.UpcomingEnemyNames.Add(enemy.gameObject.name);
                    }
                }
                _log.LogInfo($"[EnemyProvider] Upcoming enemies: {snapshot.UpcomingEnemyNames.Count}");
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"EnemyStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }
}
