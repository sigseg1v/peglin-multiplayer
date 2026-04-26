using System;
using Multipeglin.GameState;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.State;

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
            log?.LogError($"PlayerStateSnapshotClientHandler: {ex.Message}");
        }
    }
}
