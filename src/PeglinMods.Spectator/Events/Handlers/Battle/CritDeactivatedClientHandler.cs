namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class CritDeactivatedClientHandler : IClientHandler<CritDeactivatedEvent>
{
    public void Handle(CritDeactivatedEvent networkEvent)
    {
        BattleController.onCriticalHitDeactivated?.Invoke();
    }
}
