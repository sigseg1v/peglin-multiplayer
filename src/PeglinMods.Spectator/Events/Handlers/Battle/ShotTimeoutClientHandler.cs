namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class ShotTimeoutClientHandler : IClientHandler<ShotTimeoutEvent>
{
    public void Handle(ShotTimeoutEvent networkEvent)
    {
        BattleController.OnShotTimeout?.Invoke();
    }
}
