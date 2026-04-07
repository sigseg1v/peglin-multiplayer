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
            // In coop mode, discards only affect the active player's deck on the host.
            // Don't invoke on the client — it would discard from the CLIENT's own deck.
            if (UI.LobbyUI.GameStartReceived) return;

            BattleController.OnOrbDiscarded?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"OrbDiscarded handler failed: {e.Message}");
        }
    }
}
