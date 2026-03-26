namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class AttackStartedClientHandler : IClientHandler<AttackStartedEvent>
{
    public void Handle(AttackStartedEvent networkEvent)
    {
        BattleController.OnAttackStarted?.Invoke();
    }
}
