using System;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class MapStateSnapshotClientHandler : IClientHandler<MapStateSnapshot>
{
    public void Handle(MapStateSnapshot e)
    {
        var log = MultiplayerPlugin.Logger;
        try
        {
            var mode = MultiplayerPlugin.Services?.Resolve<IMultiplayerMode>();
            if (mode?.ClientMode == ClientMode.Mirror)
            {
                var applyService = MultiplayerPlugin.Services?.Resolve<GameStateApplyService>();
                applyService?.ApplyMapState(e);
                return;
            }

            // Diagnostics mode: log
            log?.LogInfo("[StateSync] MapState received (diagnostics mode)");
        }
        catch (Exception ex)
        {
            log?.LogError("MapStateSnapshotClientHandler: {ex.Message}");
        }
    }
}
