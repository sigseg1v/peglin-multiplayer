using Battle;
using BepInEx.Logging;
using Multipeglin.Events.Network.Health;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Subscriptions;

public sealed class HealthSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;
    private readonly CoopStateManager _coopStateManager;

    public HealthSubscriptions(
        IGameEventRegistry registry,
        ManualLogSource log,
        CoopStateManager coopStateManager = null)
    {
        _registry = registry;
        _log = log;
        _coopStateManager = coopStateManager;
    }

    private static bool IsHosting =>
        MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting;

    public void Subscribe()
    {
        PlayerHealthController.OnPlayerDamaged += OnPlayerDamaged;
        PlayerHealthController.OnPlayerHealed += OnPlayerHealed;
        PlayerHealthController.OnArmourHit += OnArmourHit;
        PlayerHealthController.OnDodge += OnDodge;
        PlayerHealthController.OnHealthDepleted += OnHealthDepleted;
        PlayerHealthController.OnPlayerMaxHealthChanged += OnMaxHealthChanged;
        _log.LogInfo("HealthSubscriptions registered");
    }

    public void Unsubscribe()
    {
        PlayerHealthController.OnPlayerDamaged -= OnPlayerDamaged;
        PlayerHealthController.OnPlayerHealed -= OnPlayerHealed;
        PlayerHealthController.OnArmourHit -= OnArmourHit;
        PlayerHealthController.OnDodge -= OnDodge;
        PlayerHealthController.OnHealthDepleted -= OnHealthDepleted;
        PlayerHealthController.OnPlayerMaxHealthChanged -= OnMaxHealthChanged;
    }

    private void OnPlayerDamaged(float amount)
    {
        if (!IsHosting)
        {
            return;
        }

        // PlayerHealthController.Damage fires OnPlayerDamaged?.Invoke before its
        // early-return guards, so zero-amount damage triggers reach us. Skip
        // those to avoid pointless JSON-serialize+broadcast on every no-op.
        if (amount <= 0f)
        {
            return;
        }

        // In coop, distribute this damage to every non-active player immediately.
        // Some damage sources (red bomb detonations, delayed effects) fire outside
        // the OnAttackStarted/OnTurnComplete delta window, so the reactive hook is
        // the only path that captures them for non-active players.
        CoopSubscriptions.Instance?.HandleImmediateDamage(amount);

        _registry.Dispatch(new PlayerDamagedEvent
        {
            Damage = amount,
            RemainingHealth = GetCurrentHealth(),
            MaxHealth = GetMaxHealth()
        });
    }

    private void OnPlayerHealed(float amount)
    {
        if (!IsHosting)
        {
            return;
        }

        // PlayerHealthController.Heal fires OnPlayerHealed?.Invoke before its
        // early-return guards (VampireOrb calls Heal per peg hit even when the
        // computed amount is zero). Skip those before any further work.
        if (amount <= 0f)
        {
            return;
        }

        // Mirror the heal into the active player's CoopPlayerState immediately.
        // Heals from sources that fire outside SaveActivePlayerState boundaries
        // (Doctorb proc, lifesteal effects) would otherwise be lost on the next
        // turn swap when singletons get snapshotted into the per-slot state.
        CoopSubscriptions.Instance?.HandleImmediateHeal(amount);

        _registry.Dispatch(new PlayerHealedEvent
        {
            Amount = amount,
            RemainingHealth = GetCurrentHealth(),
            TargetSlotIndex = _coopStateManager?.ActivePlayerSlot ?? -1
        });
    }

    private void OnArmourHit(float amount)
    {
        if (!IsHosting)
        {
            return;
        }

        _registry.Dispatch(new ArmourHitEvent { Damage = amount });
    }

    private void OnDodge(float info)
    {
        if (!IsHosting)
        {
            return;
        }

        _registry.Dispatch(new DodgeEvent { DodgeInfo = info });
    }

    private void OnHealthDepleted()
    {
        if (!IsHosting)
        {
            return;
        }

        _registry.Dispatch(new HealthDepletedEvent());
    }

    private void OnMaxHealthChanged(float newMax)
    {
        if (!IsHosting)
        {
            return;
        }

        _registry.Dispatch(new MaxHealthChangedEvent { NewMaxHealth = newMax });
    }

    // cached once per battle: FindObjectOfType is a scene-wide scan, and
    // GetCurrentHealth/GetMaxHealth fire on every player damage/heal — with
    // per-peg heal effects this hit thousands of scene scans per shot.
    // Unity's overloaded == handles destroyed-object detection so we refetch
    // automatically across scene transitions.
    private static PlayerHealthController _cachedController;

    private static PlayerHealthController GetController()
    {
        if (_cachedController != null)
        {
            return _cachedController;
        }

        _cachedController = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
        return _cachedController;
    }

    private static float GetCurrentHealth()
    {
        var controller = GetController();
        return controller != null ? controller.CurrentHealth : 0f;
    }

    private static float GetMaxHealth()
    {
        var controller = GetController();
        return controller != null ? controller.MaxHealth : 0f;
    }
}
