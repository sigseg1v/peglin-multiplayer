namespace PeglinMods.Spectator.Events.Handlers.Health;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Health;

public sealed class DodgeClientHandler : IClientHandler<DodgeEvent>
{
    public void Handle(DodgeEvent networkEvent)
    {
        PlayerHealthController.OnDodge?.Invoke(networkEvent.DodgeInfo);
    }
}
