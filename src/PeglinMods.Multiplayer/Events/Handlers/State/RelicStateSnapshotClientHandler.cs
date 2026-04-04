using System;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class RelicStateSnapshotClientHandler : IClientHandler<RelicStateSnapshot>
{
    public void Handle(RelicStateSnapshot e)
    {
        var log = MultiplayerPlugin.Logger;
        try
        {
            // In coop mode, each player has their own relics — don't overwrite
            if (UI.LobbyUI.GameStartReceived) return;

            var mode = MultiplayerPlugin.Services?.Resolve<IMultiplayerMode>();
            if (mode?.ClientMode == ClientMode.Mirror)
            {
                var applyService = MultiplayerPlugin.Services?.Resolve<GameStateApplyService>();
                applyService?.ApplyRelicState(e);
                return;
            }

            // Diagnostics mode: log
            log?.LogInfo("[StateSync] RelicState received (diagnostics mode)");
        }
        catch (Exception ex)
        {
            log?.LogError("RelicStateSnapshotClientHandler: {ex.Message}");
        }
    }
}
