namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class ReloadStartedClientHandler : IClientHandler<ReloadStartedEvent>
{
    public void Handle(ReloadStartedEvent networkEvent)
    {
        BattleController.OnReloadStarted?.Invoke();
    }
}
