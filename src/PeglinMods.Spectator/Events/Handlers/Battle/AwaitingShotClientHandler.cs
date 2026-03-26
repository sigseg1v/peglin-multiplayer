namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class AwaitingShotClientHandler : IClientHandler<AwaitingShotEvent>
{
    public void Handle(AwaitingShotEvent networkEvent)
    {
        BattleController.OnStartedAwaitingShot?.Invoke();
    }
}
