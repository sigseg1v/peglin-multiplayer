namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class RoundIncrementedClientHandler : IClientHandler<RoundIncrementedEvent>
{
    public void Handle(RoundIncrementedEvent networkEvent)
    {
        BattleController.OnRoundCountIncremented?.Invoke(networkEvent.RoundCount);
    }
}
