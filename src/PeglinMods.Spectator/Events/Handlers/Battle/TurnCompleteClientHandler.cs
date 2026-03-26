namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class TurnCompleteClientHandler : IClientHandler<TurnCompleteEvent>
{
    public void Handle(TurnCompleteEvent networkEvent)
    {
        BattleController.OnTurnComplete?.Invoke();
    }
}
