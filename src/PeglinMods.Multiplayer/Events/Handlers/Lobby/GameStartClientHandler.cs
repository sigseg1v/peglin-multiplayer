namespace PeglinMods.Multiplayer.Events.Handlers.Lobby;

using System;
using PeglinMods.Multiplayer.Events.Network.Lobby;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.UI;

/// <summary>
/// Client receives GameStartEvent: store the final player list and
/// let the host drive scene transitions.
/// </summary>
public sealed class GameStartClientHandler : IClientHandler<GameStartEvent>
{
    public void Handle(GameStartEvent networkEvent)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            MultiplayerPlugin.Logger?.LogInfo($"[GameStart] Received game start with {networkEvent.FinalPlayers?.Count ?? 0} players");

            // Store final player list for CoopStateManager to use
            LobbyUI.OnGameStartReceived(networkEvent);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[GameStart] Handler failed: {e.Message}");
        }
    }
}
