using System.Reflection;
using Battle;
using Battle.Attacks;
using Cruciball;
using HarmonyLib;
using Relics;
using UnityEngine;

namespace Multipeglin.Events.Subscriptions.Coop;

/// <summary>
/// Default reflection-based implementation of IBattleControllerUpdateManager.
/// Resolves AccessTools.Field handles once at construction time so every call
/// avoids re-walking the type metadata. Field names mirror the private
/// BattleController members exactly.
/// </summary>
internal sealed class BattleControllerUpdateManager : IBattleControllerUpdateManager
{
    // Per-shot tally fields. _criticalHitCount is static (instance == null).
    private static readonly FieldInfo PegTallyField = AccessTools.Field(typeof(BattleController), "_pegMultiplierDamageTally");
    private static readonly FieldInfo CritField = AccessTools.Field(typeof(BattleController), "_criticalHitCount");
    private static readonly FieldInfo NumPegsField = AccessTools.Field(typeof(BattleController), "_numPegsHit");
    private static readonly FieldInfo CactusField = AccessTools.Field(typeof(BattleController), "_cactusDamageTally");
    private static readonly FieldInfo DamageMultField = AccessTools.Field(typeof(BattleController), "_damageMultiplier");
    private static readonly FieldInfo DamageBonusField = AccessTools.Field(typeof(BattleController), "_damageBonus");

    // Active ball lifecycle.
    private static readonly FieldInfo ActiveBallField = AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
    private static readonly FieldInfo RemainingBallsField = AccessTools.Field(typeof(BattleController), "_remainingPachinkoBalls");

    // Cached singleton field handles used by Attack.Initialize.
    private static readonly FieldInfo AttackManagerField = AccessTools.Field(typeof(BattleController), "_attackManager");
    private static readonly FieldInfo DeckManagerField = AccessTools.Field(typeof(BattleController), "_deckManager");
    private static readonly FieldInfo RelicManagerField = AccessTools.Field(typeof(BattleController), "_relicManager");
    private static readonly FieldInfo CruciballManagerField = AccessTools.Field(typeof(BattleController), "_cruciballManager");
    private static readonly FieldInfo PlayerHealthField = AccessTools.Field(typeof(BattleController), "_playerHealthController");
    private static readonly FieldInfo PlayerStatusField = AccessTools.Field(typeof(BattleController), "_playerStatusEffectController");
    private static readonly FieldInfo PlayerTransformField = AccessTools.Field(typeof(BattleController), "_playerTransform");

    // AttackManager._attack is private; the coop reset path swaps it directly.
    private static readonly FieldInfo AttackManagerAttackField = AccessTools.Field(typeof(AttackManager), "_attack");

    private static readonly MethodInfo DrawBallMethod = AccessTools.Method(typeof(BattleController), "DrawBall");

    public BattleController GetBattleController()
    {
        return Object.FindObjectOfType<BattleController>();
    }

    public int GetPegMultiplierDamageTally(BattleController bc)
    {
        return bc != null && PegTallyField != null ? (int)PegTallyField.GetValue(bc) : 0;
    }

    public int GetCriticalHitCount()
    {
        return CritField != null ? (int)CritField.GetValue(null) : 0;
    }

    public int GetNumPegsHit(BattleController bc)
    {
        return bc != null && NumPegsField != null ? (int)NumPegsField.GetValue(bc) : 0;
    }

    public int GetCactusDamageTally(BattleController bc)
    {
        return bc != null && CactusField != null ? (int)CactusField.GetValue(bc) : 0;
    }

    public float GetDamageMultiplier(BattleController bc)
    {
        return bc != null && DamageMultField != null ? (float)DamageMultField.GetValue(bc) : 1f;
    }

    public long GetDamageBonus(BattleController bc)
    {
        return bc != null && DamageBonusField != null ? (int)DamageBonusField.GetValue(bc) : 0;
    }

    public void SetPegMultiplierDamageTally(BattleController bc, int value)
    {
        PegTallyField?.SetValue(bc, value);
    }

    public void SetCriticalHitCount(int value)
    {
        CritField?.SetValue(null, value);
    }

    public void SetNumPegsHit(BattleController bc, int value)
    {
        NumPegsField?.SetValue(bc, value);
    }

    public void SetCactusDamageTally(BattleController bc, int value)
    {
        CactusField?.SetValue(bc, value);
    }

    public void SetDamageMultiplier(BattleController bc, float value)
    {
        DamageMultField?.SetValue(bc, value);
    }

    public void SetDamageBonus(BattleController bc, long value)
    {
        // Game stores _damageBonus as int; preserve that contract.
        DamageBonusField?.SetValue(bc, (int)value);
    }

    public void ResetShotTallies(BattleController bc)
    {
        if (bc == null)
        {
            return;
        }

        PegTallyField?.SetValue(bc, 0);
        CritField?.SetValue(null, 0);
        NumPegsField?.SetValue(bc, 0);
        CactusField?.SetValue(bc, 0);
        DamageMultField?.SetValue(bc, 1f);
        DamageBonusField?.SetValue(bc, 0);
    }

    public void WriteShotTallies(
        BattleController bc,
        int pegTally,
        int crits,
        int numPegs,
        int cactus,
        float dmgMult,
        long dmgBonus)
    {
        if (bc == null)
        {
            return;
        }

        PegTallyField?.SetValue(bc, pegTally);
        CritField?.SetValue(null, crits);
        NumPegsField?.SetValue(bc, numPegs);
        CactusField?.SetValue(bc, cactus);
        DamageMultField?.SetValue(bc, dmgMult);
        DamageBonusField?.SetValue(bc, (int)dmgBonus);
    }

    public GameObject GetActivePachinkoBall(BattleController bc)
    {
        if (bc == null)
        {
            return null;
        }

        return ActiveBallField?.GetValue(bc) as GameObject;
    }

    public void DestroyActivePachinkoBall(BattleController bc)
    {
        if (bc == null || ActiveBallField == null)
        {
            return;
        }

        if (ActiveBallField.GetValue(bc) is GameObject activeBall)
        {
            Object.Destroy(activeBall);
        }

        ActiveBallField.SetValue(bc, null);
    }

    public void SetRemainingPachinkoBalls(BattleController bc, int value)
    {
        RemainingBallsField?.SetValue(bc, value);
    }

    public AttackManager GetAttackManager(BattleController bc)
    {
        return bc != null ? AttackManagerField?.GetValue(bc) as AttackManager : null;
    }

    public Attack GetCurrentAttack(AttackManager am)
    {
        return am != null ? AttackManagerAttackField?.GetValue(am) as Attack : null;
    }

    public void SetCurrentAttack(AttackManager am, Attack attack)
    {
        if (am == null)
        {
            return;
        }

        AttackManagerAttackField?.SetValue(am, attack);
    }

    public DeckManager GetDeckManager(BattleController bc)
    {
        return bc != null ? DeckManagerField?.GetValue(bc) as DeckManager : null;
    }

    public RelicManager GetRelicManager(BattleController bc)
    {
        return bc != null ? RelicManagerField?.GetValue(bc) as RelicManager : null;
    }

    public CruciballManager GetCruciballManager(BattleController bc)
    {
        return bc != null ? CruciballManagerField?.GetValue(bc) as CruciballManager : null;
    }

    public PlayerHealthController GetPlayerHealthController(BattleController bc)
    {
        return bc != null ? PlayerHealthField?.GetValue(bc) as PlayerHealthController : null;
    }

    public Battle.StatusEffects.PlayerStatusEffectController GetPlayerStatusEffectController(BattleController bc)
    {
        return bc != null ? PlayerStatusField?.GetValue(bc) as Battle.StatusEffects.PlayerStatusEffectController : null;
    }

    public Transform GetPlayerTransform(BattleController bc)
    {
        return bc != null ? PlayerTransformField?.GetValue(bc) as Transform : null;
    }

    public void SetBattleState(BattleController.BattleState state)
    {
        BattleController.CurrentBattleState = state;
    }

    public void InvokeDrawBall(BattleController bc)
    {
        if (bc == null || DrawBallMethod == null)
        {
            return;
        }

        DrawBallMethod.Invoke(bc, null);
    }
}
