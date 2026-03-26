namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class BombDetonatedClientHandler : IClientHandler<BombDetonatedEvent>
{
    public void Handle(BombDetonatedEvent networkEvent)
    {
        BattleController.OnBombDetonated?.Invoke();
    }
}
