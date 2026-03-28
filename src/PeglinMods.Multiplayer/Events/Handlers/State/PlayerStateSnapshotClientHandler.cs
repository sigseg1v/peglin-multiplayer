using System;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class PlayerStateSnapshotClientHandler : IClientHandler<PlayerStateSnapshot>
{
    public void Handle(PlayerStateSnapshot e)
    {
        var log = MultiplayerPlugin.Logger;
        try
        {
            var mode = MultiplayerPlugin.Services?.Resolve<IMultiplayerMode>();
            if (mode?.ClientMode == ClientMode.Mirror)
            {
                var applyService = MultiplayerPlugin.Services?.Resolve<GameStateApplyService>();
                applyService?.ApplyPlayerState(e);
                return;
            }

            // Diagnostics mode: log
            log?.LogInfo("[StateSync] PlayerState received (diagnostics mode)");
        }
        catch (Exception ex)
        {
            log?.LogError("PlayerStateSnapshotClientHandler: {ex.Message}");
        }
    }
}
