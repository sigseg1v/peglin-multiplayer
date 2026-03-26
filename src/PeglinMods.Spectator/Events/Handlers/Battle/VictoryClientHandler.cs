namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class VictoryClientHandler : IClientHandler<VictoryEvent>
{
    public void Handle(VictoryEvent networkEvent)
    {
        BattleController.OnVictory?.Invoke();
    }
}
