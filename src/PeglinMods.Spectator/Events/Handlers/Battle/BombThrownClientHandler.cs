namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class BombThrownClientHandler : IClientHandler<BombThrownEvent>
{
    public void Handle(BombThrownEvent networkEvent)
    {
        BattleController.OnBombThrown?.Invoke();
    }
}
