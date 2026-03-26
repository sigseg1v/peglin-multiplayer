namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class OrbDiscardedClientHandler : IClientHandler<OrbDiscardedEvent>
{
    public void Handle(OrbDiscardedEvent networkEvent)
    {
        try
        {
            BattleController.OnOrbDiscarded?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"OrbDiscarded handler failed: {e.Message}");
        }
    }
}
