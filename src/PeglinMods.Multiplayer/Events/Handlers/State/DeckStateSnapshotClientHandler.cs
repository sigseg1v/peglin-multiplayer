using System;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class DeckStateSnapshotClientHandler : IClientHandler<DeckStateSnapshot>
{
    public void Handle(DeckStateSnapshot e)
    {
        var log = MultiplayerPlugin.Logger;
        try
        {
            // In coop mode, each player has their own deck — don't overwrite
            if (UI.LobbyUI.GameStartReceived) return;

            var mode = MultiplayerPlugin.Services?.Resolve<IMultiplayerMode>();
            if (mode?.ClientMode == ClientMode.Mirror)
            {
                var applyService = MultiplayerPlugin.Services?.Resolve<GameStateApplyService>();
                applyService?.ApplyDeckState(e);
                return;
            }

            // Diagnostics mode: log
            log?.LogInfo("[StateSync] DeckState received (diagnostics mode)");
        }
        catch (Exception ex)
        {
            log?.LogError("DeckStateSnapshotClientHandler: {ex.Message}");
        }
    }
}
