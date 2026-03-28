namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

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
            MultiplayerPlugin.Logger.LogWarning($"OrbDiscarded handler failed: {e.Message}");
        }
    }
}
