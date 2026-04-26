using Battle;
using HarmonyLib;
using Multipeglin.Events.Network.Battle;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Subscriptions;

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
        // Subscribe to OnHealthDepleted, not OnDefeat — OnDefeat is declared on
        // PlayerHealthController but never actually invoked anywhere. OnHealthDepleted
        // fires from CheckForDeathAndUpdateBar when HP <= 0 and is what BattleController
        // itself uses as the real defeat signal.
        PlayerHealthController.OnHealthDepleted += OnDefeat;
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
        PlayerHealthController.OnHealthDepleted -= OnDefeat;
    }

    private void OnBattleStarted()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new BattleStartedEvent());
        }
    }

    private void OnBattleEnded()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new BattleEndedEvent());
        }
    }

    private void OnVictory()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new VictoryEvent());
        }
    }

    private void OnAttackStarted()
    {
        if (!_multiplayerMode.IsHosting)
        {
            return;
        }
        // When the coop DoAttack sequencer is running, it dispatches per-slot
        // AttackStartedEvents itself. Suppress the generic delegate-driven
        // dispatch that would otherwise fire from StartAttacking() and produce
        // a duplicate visual on clients with stale cached values.
        if (Patches.MultiplayerClientPatches.SuppressOnAttackStartedDispatch)
        {
            return;
        }

        _registry.Dispatch(new AttackStartedEvent
        {
            AnimTrigger = Patches.MultiplayerClientPatches.LastAttackAnimTrigger ?? "attack",
            TargetEnemyGuid = Patches.MultiplayerClientPatches.LastAttackTargetGuid,
            NumPegsHit = Patches.MultiplayerClientPatches.LastAttackNumPegsHit,
            IsCrit = Patches.MultiplayerClientPatches.LastAttackIsCrit,
            OrbName = Patches.MultiplayerClientPatches.LastAttackOrbName,
        });
    }

    private void OnTurnComplete()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new TurnCompleteEvent());
        }
    }

    private void OnShotComplete()
    {
        if (!_multiplayerMode.IsHosting)
        {
            return;
        }
        // Force-fade any LongPeg the host hit during this shot (collider still
        // enabled, _hit=true, gray). The native game leaves them gray indefinitely
        // unless the 0.5s _beingHit timer or the 5-bounce path fires; the user
        // wants a clean fade between player turns. SetActiveStatus(false) disables
        // the collider and triggers LongPeg_SetActiveStatus_Postfix → RemoveIfCleared,
        // which fades alpha→0 and deactivates. Heartbeat propagates the
        // collider-disabled state to clients (IsCleared=true → applier fades).
        try
        {
            var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
            var pm = bc?.pegManager;
            if (pm?.allPegs != null)
            {
                var hitField = AccessTools.Field(typeof(LongPeg), "_hit");
                foreach (var peg in pm.allPegs)
                {
                    if (peg is LongPeg longPeg && longPeg.gameObject.activeSelf)
                    {
                        var isHit = (bool)(hitField?.GetValue(longPeg) ?? false);
                        if (isHit && !longPeg.IsDisabled())
                        {
                            longPeg.SetActiveStatus(active: false);
                        }
                    }
                }
            }
        }
        catch { }

        _registry.Dispatch(new ShotCompleteEvent());
    }

    private void OnRoundIncremented(int roundCount)
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new RoundIncrementedEvent { RoundCount = roundCount });
        }
    }

    private void OnReloadStarted()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new ReloadStartedEvent());
        }
    }

    private void OnCritActivated()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new CritActivatedEvent());
        }
    }

    private void OnCritDeactivated()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new CritDeactivatedEvent());
        }
    }

    private static readonly System.Reflection.FieldInfo _bombsRegularField =
        AccessTools.Field(typeof(BattleController), "_bombsToThrowRegular");

    private static readonly System.Reflection.FieldInfo _bombsRiggedField =
        AccessTools.Field(typeof(BattleController), "_bombsToThrowRigged");

    private void OnBombThrown()
    {
        if (!_multiplayerMode.IsHosting)
        {
            return;
        }

        int regular = 0, rigged = 0;
        try
        {
            var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
            if (bc != null)
            {
                regular = (int)(_bombsRegularField?.GetValue(bc) ?? 0);
                rigged = (int)(_bombsRiggedField?.GetValue(bc) ?? 0);
            }
        }
        catch { }

        _registry.Dispatch(new BombThrownEvent { RegularCount = regular, RiggedCount = rigged });
    }

    private void OnBombDetonated()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new BombDetonatedEvent());
        }
    }

    private void OnOrbDiscarded()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new OrbDiscardedEvent());
        }
    }

    private void OnAwaitingShot()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new AwaitingShotEvent());
        }
    }

    private void OnShotTimeout()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new ShotTimeoutEvent());
        }
    }

    private void OnDefeat()
    {
        if (_multiplayerMode.IsHosting)
        {
            _registry.Dispatch(new DefeatEvent());
        }
    }
}
