using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Utility;

namespace Multipeglin.GameState.Providers;

public class EnemyStateProvider : IGameStateProvider<EnemyStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly EnemyIdentifier _enemyId;

    // Cached reflection handles. Resolving FieldInfo via AccessTools.Field for
    // every enemy every heartbeat (4 fields * N enemies) was a measurable share
    // of capture cost — cache once at type init.
    private static readonly System.Reflection.FieldInfo _bcBattleStateField
        = AccessTools.Field(typeof(Battle.BattleController), "_battleState");

    private static readonly System.Reflection.FieldInfo _emEnemiesField
        = AccessTools.Field(typeof(EnemyManager), "Enemies");

    private static readonly System.Reflection.FieldInfo _enemyMaxHealthField
        = AccessTools.Field(typeof(Battle.Enemies.Enemy), "_maxHealth");

    private static readonly System.Reflection.FieldInfo _enemyChargeLenField
        = AccessTools.Field(typeof(Battle.Enemies.Enemy), "AttackChargeLength");

    private static readonly System.Reflection.FieldInfo _enemyStatusEffectsField
        = AccessTools.Field(typeof(Battle.Enemies.Enemy), "_statusEffects");

    private static readonly System.Reflection.FieldInfo _updateSliderBgField
        = AccessTools.Field(typeof(UpdateSlider), "_background");

    private static readonly System.Reflection.FieldInfo _updateSliderC19Field
        = AccessTools.Field(typeof(UpdateSlider), "_c19Background");

    private static readonly System.Reflection.FieldInfo _enemyInfoElementsField
        = AccessTools.Field(typeof(Battle.EnemyInfoManager), "_enemyInfoElements");

    private static readonly System.Reflection.FieldInfo _enemyInfoElementEnemyField
        = AccessTools.Field(typeof(Battle.EnemyInfoElement), "_enemy");

    // Per-status-effect-type FieldInfo cache (EffectType + Intensity). Reused
    // across captures so reflection only runs once per concrete StatusEffect
    // subclass we encounter.
    private static readonly Dictionary<Type, (System.Reflection.FieldInfo type, System.Reflection.FieldInfo intensity)>
        _statusEffectFieldCache = new Dictionary<Type, (System.Reflection.FieldInfo, System.Reflection.FieldInfo)>(8);

    // Suppresses identical per-capture log lines (per-enemy + upcoming queue).
    private string _lastCaptureLogSig;

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
                if (_bcBattleStateField != null)
                {
                    var state = _bcBattleStateField.GetValue(null); // static field
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
                var enemiesList = _emEnemiesField?.GetValue(em) as List<Battle.Enemies.Enemy>;

                if (enemiesList != null)
                {
                    for (var i = 0; i < enemiesList.Count; i++)
                    {
                        var enemy = enemiesList[i];
                        if (enemy == null)
                        {
                            continue;
                        }

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
                        if (_enemyMaxHealthField != null)
                        {
                            entry.MaxHealth = (float)_enemyMaxHealthField.GetValue(enemy);
                        }

                        // Charge
                        entry.CurrentCharge = enemy.currentChargeTime;
                        if (_enemyChargeLenField != null)
                        {
                            entry.ChargeTime = (int)_enemyChargeLenField.GetValue(enemy);
                        }

                        // Cruciball-extra HP bar — detect by comparing UpdateSlider's
                        // current background sprite to its _c19Background slot.
                        try
                        {
                            var slider = enemy.HealthBarBarSprite;
                            if (slider != null)
                            {
                                var bgImg = _updateSliderBgField?.GetValue(slider) as UnityEngine.UI.Image;
                                var c19Sprite = _updateSliderC19Field?.GetValue(slider) as UnityEngine.Sprite;
                                if (bgImg != null && c19Sprite != null && bgImg.sprite == c19Sprite)
                                {
                                    entry.IsC19Extra = true;
                                }
                            }
                        }
                        catch
                        {
                        }

                        // Shield barricade (ShieldKnight etc). BarricadeEnemy is a separate
                        // Enemy instance wired via ShieldEnemy._shield — not in EnemyManager.Enemies,
                        // so we attach its state to the parent's entry.
                        try
                        {
                            if (enemy is ShieldEnemy se && se.shield != null)
                            {
                                var shield = se.shield;
                                entry.HasShield = true;
                                entry.ShieldCurrentHealth = shield.CurrentHealth;
                                if (_enemyMaxHealthField != null)
                                {
                                    entry.ShieldMaxHealth = (float)_enemyMaxHealthField.GetValue(shield);
                                }

                                entry.ShieldActive = shield.gameObject.activeInHierarchy;
                            }
                        }
                        catch
                        {
                        }

                        // Status effects
                        try
                        {
                            var effects = _enemyStatusEffectsField?.GetValue(enemy) as System.Collections.IList;
                            if (effects != null)
                            {
                                entry.StatusEffects = new List<StatusEffectEntry>();
                                foreach (var eff in effects)
                                {
                                    var fields = GetStatusEffectFields(eff.GetType());
                                    if (fields.type != null)
                                    {
                                        var et = fields.type.GetValue(eff);
                                        entry.StatusEffects.Add(new StatusEffectEntry
                                        {
                                            EffectType = (int)et,
                                            EffectName = et.ToString(),
                                            Intensity = (int)(fields.intensity?.GetValue(eff) ?? 0),
                                        });
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }

                        snapshot.Enemies.Add(entry);
                    }
                }
            }

            // Capture upcoming enemy names from EnemyInfoManager._enemyInfoElements.
            // This is the VISUAL queue — only contains enemies not yet on the battlefield.
            // _upcomingSpawns contains ALL wave data including already-spawned entries.
            var eim = UnityEngine.Object.FindObjectOfType<Battle.EnemyInfoManager>();
            if (eim != null)
            {
                var elements = _enemyInfoElementsField?.GetValue(eim) as System.Collections.Generic.Queue<Battle.EnemyInfoElement>;
                if (elements != null && elements.Count > 0)
                {
                    foreach (var element in elements)
                    {
                        if (element == null)
                        {
                            continue;
                        }

                        var enemy = _enemyInfoElementEnemyField?.GetValue(element) as Battle.Enemies.Enemy;
                        if (enemy != null)
                        {
                            snapshot.UpcomingEnemyNames.Add(enemy.gameObject.name);
                        }
                    }
                }
            }

            // Gate diagnostics on a content signature so we only emit log spam
            // when something actually changes — at 1-2s heartbeat cadence with
            // 4-7 enemies this previously produced ~5-10 LogInfo lines per tick.
            var captureSig = BuildCaptureLogSignature(snapshot);
            if (captureSig != _lastCaptureLogSig)
            {
                _lastCaptureLogSig = captureSig;
                foreach (var entry in snapshot.Enemies)
                {
                    var effectStr = entry.StatusEffects?.Count > 0
                        ? string.Join(",", entry.StatusEffects.ConvertAll(e => $"{e.EffectName}={e.Intensity}"))
                        : "none";
                    var shieldStr = entry.HasShield
                        ? $" shield={entry.ShieldCurrentHealth:F0}/{entry.ShieldMaxHealth:F0}{(entry.ShieldActive ? string.Empty : " (dead)")}"
                        : string.Empty;
                    _log.LogInfo($"[EnemyProvider] Captured enemy: guid={entry.Id} loc={entry.LocKey} name={entry.EnemyName} " +
                        $"hp={entry.CurrentHealth}/{entry.MaxHealth} pos=({entry.PosX:F1},{entry.PosY:F1}) slot={entry.SlotIndex} effects=[{effectStr}]{shieldStr}");
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

    private static (System.Reflection.FieldInfo type, System.Reflection.FieldInfo intensity) GetStatusEffectFields(Type t)
    {
        if (_statusEffectFieldCache.TryGetValue(t, out var cached))
        {
            return cached;
        }

        cached = (AccessTools.Field(t, "EffectType"), AccessTools.Field(t, "Intensity"));
        _statusEffectFieldCache[t] = cached;
        return cached;
    }

    private static string BuildCaptureLogSignature(EnemyStateSnapshot snapshot)
    {
        // Cheap signature over enemy hp + status-effect intensities + upcoming
        // count. Avoids logging when nothing meaningful changed.
        var sb = new System.Text.StringBuilder(64);
        sb.Append(snapshot.UpcomingEnemyNames?.Count ?? 0);
        if (snapshot.Enemies != null)
        {
            foreach (var e in snapshot.Enemies)
            {
                sb.Append('|').Append(e.Id).Append(':')
                  .Append(e.CurrentHealth).Append('/').Append(e.MaxHealth)
                  .Append(',').Append(e.HasShield ? 1 : 0)
                  .Append('/').Append(e.ShieldCurrentHealth);
                if (e.StatusEffects != null)
                {
                    foreach (var se in e.StatusEffects)
                    {
                        sb.Append(';').Append(se.EffectType).Append('=').Append(se.Intensity);
                    }
                }
            }
        }

        return sb.ToString();
    }
}
