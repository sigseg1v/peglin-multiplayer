using System;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class EnemyStateSnapshotClientHandler : IClientHandler<EnemyStateSnapshot>
{
    public void Handle(EnemyStateSnapshot e)
    {
        var log = MultiplayerPlugin.Logger;
        try
        {
            var mode = MultiplayerPlugin.Services?.Resolve<IMultiplayerMode>();
            if (mode?.ClientMode == ClientMode.Mirror)
            {
                var applyService = MultiplayerPlugin.Services?.Resolve<GameStateApplyService>();
                applyService?.ApplyEnemyState(e);
                return;
            }

            // Diagnostics mode: log
            log?.LogInfo("[StateSync] EnemyState received (diagnostics mode)");
        }
        catch (Exception ex)
        {
            log?.LogError("EnemyStateSnapshotClientHandler: {ex.Message}");
        }
    }
}
