using System;
using Multipeglin.GameState;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Ball;

public sealed class BallStateSnapshotClientHandler : IClientHandler<BallStateSnapshot>
{
    public void Handle(BallStateSnapshot e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating)
                return;

            ClientBallRenderer.Instance?.ApplyBallSnapshot(e);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"BallStateSnapshot handler failed: {ex.Message}");
        }
    }
}
