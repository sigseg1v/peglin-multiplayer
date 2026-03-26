using System;
using Battle;
using BepInEx.Logging;
using PeglinMods.Spectator.Events.Network.Health;

namespace PeglinMods.Spectator.Events.Subscriptions;

public class HealthSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    private PlayerHealthController.PlayerHealthEvent _onPlayerDamaged;
    private PlayerHealthController.PlayerHealthEvent _onPlayerHealed;
    private PlayerHealthController.PlayerHealthEvent _onArmourHit;
    private PlayerHealthController.PlayerHealthEvent _onDodge;
    private PlayerHealthController.HealthEvent _onHealthDepleted;
    private PlayerHealthController.PlayerHealthEvent _onMaxHealthChanged;

    public HealthSubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    public void Subscribe()
    {
        _onPlayerDamaged = (float amount) =>
        {
            _registry.Dispatch(new PlayerDamagedEvent
            {
                Damage = amount,
                RemainingHealth = GetCurrentHealth(),
                MaxHealth = GetMaxHealth()
            });
        };
        PlayerHealthController.OnPlayerDamaged += _onPlayerDamaged;

        _onPlayerHealed = (float amount) =>
        {
            _registry.Dispatch(new PlayerHealedEvent
            {
                Amount = amount,
                RemainingHealth = GetCurrentHealth()
            });
        };
        PlayerHealthController.OnPlayerHealed += _onPlayerHealed;

        _onArmourHit = (float amount) =>
        {
            _registry.Dispatch(new ArmourHitEvent { Damage = amount });
        };
        PlayerHealthController.OnArmourHit += _onArmourHit;

        _onDodge = (float info) =>
        {
            _registry.Dispatch(new DodgeEvent { DodgeInfo = info });
        };
        PlayerHealthController.OnDodge += _onDodge;

        _onHealthDepleted = () =>
        {
            _registry.Dispatch(new HealthDepletedEvent());
        };
        PlayerHealthController.OnHealthDepleted += _onHealthDepleted;

        _onMaxHealthChanged = (float newMax) =>
        {
            _registry.Dispatch(new MaxHealthChangedEvent { NewMaxHealth = newMax });
        };
        PlayerHealthController.OnPlayerMaxHealthChanged += _onMaxHealthChanged;

        _log.LogInfo("HealthSubscriptions registered");
    }

    public void Unsubscribe()
    {
        PlayerHealthController.OnPlayerDamaged -= _onPlayerDamaged;
        PlayerHealthController.OnPlayerHealed -= _onPlayerHealed;
        PlayerHealthController.OnArmourHit -= _onArmourHit;
        PlayerHealthController.OnDodge -= _onDodge;
        PlayerHealthController.OnHealthDepleted -= _onHealthDepleted;
        PlayerHealthController.OnPlayerMaxHealthChanged -= _onMaxHealthChanged;
    }

    private static float GetCurrentHealth()
    {
        var controller = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
        if (controller == null)
            return 0f;
        return controller.CurrentHealth;
    }

    private static float GetMaxHealth()
    {
        var controller = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
        if (controller == null)
            return 0f;
        return controller.MaxHealth;
    }
}
