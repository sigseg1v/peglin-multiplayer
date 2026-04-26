using Battle;
using Battle.Attacks;
using Cruciball;
using Relics;
using UnityEngine;

namespace Multipeglin.Events.Subscriptions.Coop;

/// <summary>
/// Centralized reflection access to BattleController's private fields and state
/// machine controls used by the coop turn flow. Wraps the AccessTools.Field /
/// AccessTools.Method calls that were previously scattered across CoopSubscriptions
/// so the reflection details live in one place and can be mocked or replaced.
///
/// All accessors return null safely when the BattleController instance hasn't been
/// resolved yet — callers do their own null checks before continuing.
/// </summary>
public interface IBattleControllerUpdateManager
{
    /// <summary>FindObjectOfType the active BattleController.</summary>
    BattleController GetBattleController();

    // -------- Per-shot tally fields --------
    int GetPegMultiplierDamageTally(BattleController bc);

    int GetCriticalHitCount();

    int GetNumPegsHit(BattleController bc);

    int GetCactusDamageTally(BattleController bc);

    float GetDamageMultiplier(BattleController bc);

    long GetDamageBonus(BattleController bc);

    void SetPegMultiplierDamageTally(BattleController bc, int value);

    void SetCriticalHitCount(int value);

    void SetNumPegsHit(BattleController bc, int value);

    void SetCactusDamageTally(BattleController bc, int value);

    void SetDamageMultiplier(BattleController bc, float value);

    void SetDamageBonus(BattleController bc, long value);

    /// <summary>Reset every per-shot tally back to its base value (0/1f).</summary>
    void ResetShotTallies(BattleController bc);

    /// <summary>Write the host's accumulated tallies onto BattleController for the native attack pipeline.</summary>
    void WriteShotTallies(
        BattleController bc,
        int pegTally,
        int crits,
        int numPegs,
        int cactus,
        float dmgMult,
        long dmgBonus);

    // -------- Active ball lifecycle --------
    GameObject GetActivePachinkoBall(BattleController bc);

    void DestroyActivePachinkoBall(BattleController bc);

    void SetRemainingPachinkoBalls(BattleController bc, int value);

    // -------- AttackManager / Attack accessors --------
    AttackManager GetAttackManager(BattleController bc);

    Attack GetCurrentAttack(AttackManager am);

    void SetCurrentAttack(AttackManager am, Attack attack);

    // -------- Other singletons referenced by Attack.Initialize --------
    DeckManager GetDeckManager(BattleController bc);

    RelicManager GetRelicManager(BattleController bc);

    CruciballManager GetCruciballManager(BattleController bc);

    PlayerHealthController GetPlayerHealthController(BattleController bc);

    Battle.StatusEffects.PlayerStatusEffectController GetPlayerStatusEffectController(BattleController bc);

    Transform GetPlayerTransform(BattleController bc);

    // -------- State machine / methods --------
    void SetBattleState(BattleController.BattleState state);

    void InvokeDrawBall(BattleController bc);
}
