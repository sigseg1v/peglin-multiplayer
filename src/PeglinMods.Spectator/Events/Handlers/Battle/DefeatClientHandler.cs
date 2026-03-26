namespace PeglinMods.Spectator.Events.Handlers.Battle;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class DefeatClientHandler : IClientHandler<DefeatEvent>
{
    public void Handle(DefeatEvent networkEvent)
    {
        PlayerHealthController.OnDefeat?.Invoke();
    }
}
