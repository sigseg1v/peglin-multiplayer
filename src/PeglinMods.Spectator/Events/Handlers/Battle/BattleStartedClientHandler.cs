namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class BattleStartedClientHandler : IClientHandler<BattleStartedEvent>
{
    public void Handle(BattleStartedEvent networkEvent)
    {
        BattleController.OnBattleStarted?.Invoke();
    }
}
