using System;
using System.Collections.Generic;
using System.Linq;
using Battle;
using Battle.Attacks;
using Battle.Attacks.AttackBehaviours;
using BepInEx.Logging;
using UnityEngine;

namespace Multipeglin.Events.Subscriptions.Coop;

/// <summary>
/// Default IOrbStatusEffectApplier implementation. Owns the temporary off-screen
/// orb instance used to restore the host's Attack at ALL_DONE.
/// </summary>
internal sealed class OrbStatusEffectApplier : IOrbStatusEffectApplier
{
    private readonly IBattleControllerUpdateManager _bcUpdater;
    private readonly ManualLogSource _log;

    /// <summary>Temporary orb instance created at ALL_DONE; destroyed after DoAttack.</summary>
    private GameObject _tempOrbInstance;

    public OrbStatusEffectApplier(IBattleControllerUpdateManager bcUpdater, ManualLogSource log)
    {
        _bcUpdater = bcUpdater;
        _log = log;
    }

    /// <summary>
    /// Capture status effects from an orb at shot completion time for non-host players,
    /// since their effects won't be applied by the normal DoAttack pipeline (which only
    /// processes the host's Attack object).
    ///
    /// We collect from three sources matching the native per-hit pipeline:
    ///   1. attack.GetStatusEffects(critCount) — covers the Attack base class's relic
    ///      effects (ATTACKS_DEAL_BLIND, Transpherency, Poison, Exploitaball) AND the
    ///      subclass overrides that orbs like BlindOrb, ThornOrb, PoisonOrb rely on
    ///      (e.g. BlindAttack adds Blind(BlindIntensity=11)). Invoked natively per-hit
    ///      from ProjectileAttack.DoDamage; we invoke it once per shot for replay.
    ///   2. AddStatusEffectOnHit components — per-hit IAffectEnemyOnHit effects
    ///      attached to the orb prefab.
    ///   3. AddRandomStatusEffectOnHit — Roundreloquence's random roll.
    /// </summary>
    public List<(Battle.StatusEffects.StatusEffectType Type, int Intensity)> CaptureOrbStatusEffects(Attack attack, int critCount)
    {
        var effects = new List<(Battle.StatusEffects.StatusEffectType, int)>();
        try
        {
            // 1. attack.GetStatusEffects — relic-driven base effects + Attack subclass overrides
            try
            {
                var fromAttack = attack.GetStatusEffects(critCount);
                if (fromAttack != null)
                {
                    foreach (var se in fromAttack)
                    {
                        if (se == null || se.EffectType == Battle.StatusEffects.StatusEffectType.None)
                        {
                            continue;
                        }

                        effects.Add((se.EffectType, se.Intensity));
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[CoopSubs] attack.GetStatusEffects failed: {ex.Message}");
            }

            // 2. AddStatusEffectOnHit — orb-prefab per-hit effects
            foreach (var seh in attack.GetComponents<AddStatusEffectOnHit>())
            {
                var intensity = seh.intensity;
                if (seh.isCritThresholdCumulative && seh.critCountThreshold > 0)
                {
                    intensity += critCount / seh.critCountThreshold * seh.critAddIntensity;
                }
                else if (seh.critCountThreshold <= critCount)
                {
                    intensity += seh.critAddIntensity;
                }

                effects.Add((seh.type, intensity));
            }

            // 3. AddRandomStatusEffectOnHit — Roundreloquence orb
            foreach (var _ in attack.GetComponents<AddRandomStatusEffectOnHit>())
            {
                var idx = UnityEngine.Random.Range(0,
                    AddRandomStatusEffectOnHit.RoundreloquencePotentialEffects.Length);
                effects.Add((
                    AddRandomStatusEffectOnHit.RoundreloquencePotentialEffects[idx],
                    AddRandomStatusEffectOnHit.RoundreloquenceEffectIntensity[idx]));
            }

            if (effects.Count > 0)
            {
                _log?.LogInfo(
                    $"[CoopSubs] Captured {effects.Count} status effect(s) from orb " +
                    $"({attack.GetType().Name}): " +
                    string.Join(", ", effects.Select(e => $"{e.Item1}({e.Item2})")));
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[CoopSubs] CaptureOrbStatusEffects failed: {ex.Message}");
        }

        return effects.Count > 0 ? effects : null;
    }

    /// <summary>
    /// Apply self-granting post-attack status effects (Ballusion from Evasive Maneuvorb,
    /// Muscircle, Ballwark from shield pegs, etc.) to the currently loaded player's
    /// PlayerStatusEffectController. This replicates the game's native
    /// CallPostAttackOperations logic, which is skipped in coop because the DoAttack
    /// Harmony prefix returns false.
    ///
    /// Invokes the component's own HandleAttackFinished method directly so that the
    /// crit/shield-peg intensity scaling matches native behavior exactly.
    /// </summary>
    public void ApplySelfPostAttackBuffs(Attack attack, int critCount, int slotIndex)
    {
        try
        {
            var statusCtrl = UnityEngine.Object.FindObjectOfType<Battle.StatusEffects.PlayerStatusEffectController>();
            if (statusCtrl == null)
            {
                return;
            }

            var grantCount = 0;
            var grantShieldCount = 0;

            foreach (var grant in attack.GetComponents<GrantStatusAfterAttack>())
            {
                try
                {
                    grant.HandleAttackFinished(statusCtrl);
                    grantCount++;
                }
                catch (Exception ex)
                {
                    _log?.LogWarning($"[CoopSubs] GrantStatusAfterAttack.HandleAttackFinished failed: {ex.Message}");
                }
            }

            foreach (var grant in attack.GetComponents<GrantStatusAfterAttackPerShieldPeg>())
            {
                try
                {
                    grant.HandleAttackFinished(statusCtrl);
                    grantShieldCount++;
                }
                catch (Exception ex)
                {
                    _log?.LogWarning($"[CoopSubs] GrantStatusAfterAttackPerShieldPeg.HandleAttackFinished failed: {ex.Message}");
                }
            }

            if (grantCount > 0 || grantShieldCount > 0)
            {
                _log?.LogInfo(
                    $"[CoopSubs] Applied self-buffs for slot {slotIndex}: " +
                    $"GrantStatusAfterAttack={grantCount}, GrantStatusAfterAttackPerShieldPeg={grantShieldCount}, crits={critCount}");
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[CoopSubs] ApplySelfPostAttackBuffs failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Instantiate the orb prefab, initialize its Attack component, and set it as
    /// AttackManager._attack so DoAttack uses the correct damage formula.
    /// </summary>
    public void RestoreAttackFromPrefab(BattleController bc, GameObject orbPrefab, string label)
    {
        // Clean up any previous temp orb
        CleanupTempOrb();

        // Instantiate offscreen so it's invisible but active (Fire() needs active components)
        _tempOrbInstance = UnityEngine.Object.Instantiate(orbPrefab, new Vector3(-999, -999, 0), Quaternion.identity);
        _tempOrbInstance.name = $"CoopTempOrb_{label}";

        var attack = _tempOrbInstance.GetComponent<Attack>();
        if (attack == null)
        {
            _log?.LogWarning($"[CoopSubs] Prefab '{orbPrefab.name}' has no Attack component!");
            UnityEngine.Object.Destroy(_tempOrbInstance);
            _tempOrbInstance = null;
            return;
        }

        // Initialize the Attack with the current singletons (host's state is loaded at this point)
        var am = _bcUpdater.GetAttackManager(bc);
        var dm = _bcUpdater.GetDeckManager(bc);
        var rm = _bcUpdater.GetRelicManager(bc);
        var cm = _bcUpdater.GetCruciballManager(bc);
        var phc = _bcUpdater.GetPlayerHealthController(bc);
        var psec = _bcUpdater.GetPlayerStatusEffectController(bc);

        var playerTransform = _bcUpdater.GetPlayerTransform(bc);
        var playerPos = playerTransform != null ? (Vector2)playerTransform.position : Vector2.zero;

        attack.Initialize(playerPos, am, rm, dm, cm, phc, psec);

        // Clone the instanceID from the matching deck entry so that
        // IsAttackUnique can correctly identify this orb in the deck
        // (needed for UNIQUE_ORBS_BUFF / Spinventoriginality relic).
        // Initialize called SoftInit(forUI:false) which set applyUniqueBuff
        // with the wrong instanceID; we need to re-check after cloning.
        if (dm?.battleDeck != null)
        {
            foreach (var deckOrb in dm.battleDeck)
            {
                if (deckOrb != null)
                {
                    var deckAttack = deckOrb.GetComponent<Attack>();
                    if (deckAttack != null && deckAttack.locNameString == attack.locNameString)
                    {
                        attack.CloneInstanceId(deckAttack);
                        attack.CheckUniqueBuff(string.Empty);
                        break;
                    }
                }
            }
        }

        // Compute relic-based damage buffs (UNIQUE_ORBS_BUFF, INCREASE_STRENGTH_SMALL, etc.)
        // This mirrors what BattleController.DrawBall does after InitializeAttack.
        attack.CalculateStaticDamageBuffs(saveResults: true);

        // Set as the active attack in AttackManager
        if (am != null)
        {
            _bcUpdater.SetCurrentAttack(am, attack);
            am.isHeal = attack is HealAction;
        }

        _log?.LogInfo($"[CoopSubs] Restored {label} Attack from prefab: {orbPrefab.name}");
    }

    /// <summary>
    /// Clean up the temporary orb instance after DoAttack finishes.
    /// Called from the DoAttack postfix in MultiplayerClientPatches.
    /// </summary>
    public void CleanupTempOrb()
    {
        if (_tempOrbInstance != null)
        {
            UnityEngine.Object.Destroy(_tempOrbInstance);
            _tempOrbInstance = null;
        }
    }
}
