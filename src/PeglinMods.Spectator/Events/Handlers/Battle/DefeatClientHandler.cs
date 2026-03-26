namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class DefeatClientHandler : IClientHandler<DefeatEvent>
{
    public void Handle(DefeatEvent networkEvent)
    {
        try
        {
            PlayerHealthController.OnDefeat?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"Defeat handler failed: {e.Message}");
        }
    }
}
