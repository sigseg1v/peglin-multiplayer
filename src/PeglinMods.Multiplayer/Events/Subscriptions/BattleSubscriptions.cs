using System;
using Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;
using PeglinMods.Multiplayer.Multiplayer;

namespace PeglinMods.Multiplayer.Events.Subscriptions;

public sealed class BattleEventSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly IMultiplayerMode _multiplayerMode;

    public BattleEventSubscriptions(IGameEventRegistry registry, IMultiplayerMode multiplayerMode)
    {
        _registry = registry;
        _multiplayerMode = multiplayerMode;
    }

    public void Subscribe()
    {
        BattleController.OnBattleStarted += OnBattleStarted;
        BattleController.OnBattleEnded += OnBattleEnded;
        BattleController.OnVictory += OnVictory;
        BattleController.OnAttackStarted += OnAttackStarted;
        BattleController.OnTurnComplete += OnTurnComplete;
        BattleController.OnShotComplete += OnShotComplete;
        BattleController.OnRoundCountIncremented += OnRoundIncremented;
        BattleController.OnReloadStarted += OnReloadStarted;
        BattleController.onCriticalHitActivated += OnCritActivated;
        BattleController.onCriticalHitDeactivated += OnCritDeactivated;
        BattleController.OnBombThrown += OnBombThrown;
        BattleController.OnBombDetonated += OnBombDetonated;
        BattleController.OnOrbDiscarded += OnOrbDiscarded;
        BattleController.OnStartedAwaitingShot += OnAwaitingShot;
        BattleController.OnShotTimeout += OnShotTimeout;
        PlayerHealthController.OnDefeat += OnDefeat;
    }

    public void Unsubscribe()
    {
        BattleController.OnBattleStarted -= OnBattleStarted;
        BattleController.OnBattleEnded -= OnBattleEnded;
        BattleController.OnVictory -= OnVictory;
        BattleController.OnAttackStarted -= OnAttackStarted;
        BattleController.OnTurnComplete -= OnTurnComplete;
        BattleController.OnShotComplete -= OnShotComplete;
        BattleController.OnRoundCountIncremented -= OnRoundIncremented;
        BattleController.OnReloadStarted -= OnReloadStarted;
        BattleController.onCriticalHitActivated -= OnCritActivated;
        BattleController.onCriticalHitDeactivated -= OnCritDeactivated;
        BattleController.OnBombThrown -= OnBombThrown;
        BattleController.OnBombDetonated -= OnBombDetonated;
        BattleController.OnOrbDiscarded -= OnOrbDiscarded;
        BattleController.OnStartedAwaitingShot -= OnAwaitingShot;
        BattleController.OnShotTimeout -= OnShotTimeout;
        PlayerHealthController.OnDefeat -= OnDefeat;
    }

    private void OnBattleStarted() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new BattleStartedEvent()); }
    private void OnBattleEnded() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new BattleEndedEvent()); }
    private void OnVictory() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new VictoryEvent()); }
    private void OnAttackStarted() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new AttackStartedEvent()); }
    private void OnTurnComplete() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new TurnCompleteEvent()); }
    private void OnShotComplete() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new ShotCompleteEvent()); }
    private void OnRoundIncremented(int roundCount) { if (_multiplayerMode.IsHosting) _registry.Dispatch(new RoundIncrementedEvent { RoundCount = roundCount }); }
    private void OnReloadStarted() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new ReloadStartedEvent()); }
    private void OnCritActivated() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new CritActivatedEvent()); }
    private void OnCritDeactivated() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new CritDeactivatedEvent()); }
    private void OnBombThrown() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new BombThrownEvent()); }
    private void OnBombDetonated() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new BombDetonatedEvent()); }
    private void OnOrbDiscarded() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new OrbDiscardedEvent()); }
    private void OnAwaitingShot() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new AwaitingShotEvent()); }
    private void OnShotTimeout() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new ShotTimeoutEvent()); }
    private void OnDefeat() { if (_multiplayerMode.IsHosting) _registry.Dispatch(new DefeatEvent()); }
}
