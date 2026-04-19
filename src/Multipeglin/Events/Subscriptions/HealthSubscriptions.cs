using Battle;
using BepInEx.Logging;
using Multipeglin.Events.Network.Health;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Subscriptions;

public sealed class HealthSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    public HealthSubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
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
        if (!IsHosting) return;

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
        if (!IsHosting) return;
        _registry.Dispatch(new PlayerHealedEvent
        {
            Amount = amount,
            RemainingHealth = GetCurrentHealth()
        });
    }

    private void OnArmourHit(float amount)
    {
        if (!IsHosting) return;
        _registry.Dispatch(new ArmourHitEvent { Damage = amount });
    }

    private void OnDodge(float info)
    {
        if (!IsHosting) return;
        _registry.Dispatch(new DodgeEvent { DodgeInfo = info });
    }

    private void OnHealthDepleted()
    {
        if (!IsHosting) return;
        _registry.Dispatch(new HealthDepletedEvent());
    }

    private void OnMaxHealthChanged(float newMax)
    {
        if (!IsHosting) return;
        _registry.Dispatch(new MaxHealthChangedEvent { NewMaxHealth = newMax });
    }

    private static float GetCurrentHealth()
    {
        var controller = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
        return controller != null ? controller.CurrentHealth : 0f;
    }

    private static float GetMaxHealth()
    {
        var controller = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
        return controller != null ? controller.MaxHealth : 0f;
    }
}
