namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class BattleEndedClientHandler : IClientHandler<BattleEndedEvent>
{
    public void Handle(BattleEndedEvent networkEvent)
    {
        BattleController.OnBattleEnded?.Invoke();
    }
}
