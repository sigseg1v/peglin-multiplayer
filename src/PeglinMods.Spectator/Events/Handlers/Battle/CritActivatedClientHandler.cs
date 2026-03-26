namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class CritActivatedClientHandler : IClientHandler<CritActivatedEvent>
{
    public void Handle(CritActivatedEvent networkEvent)
    {
        BattleController.onCriticalHitActivated?.Invoke();
    }
}
