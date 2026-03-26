namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class ShotCompleteClientHandler : IClientHandler<ShotCompleteEvent>
{
    public void Handle(ShotCompleteEvent networkEvent)
    {
        BattleController.OnShotComplete?.Invoke();
    }
}
