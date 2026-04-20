namespace Multipeglin.Events.Handlers.Ball;

using System;
using Multipeglin.Events.Network.Ball;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;

public sealed class MultiballPositionClientHandler : IClientHandler<MultiballPositionEvent>
{
    public void Handle(MultiballPositionEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            ClientBallRenderer.Instance?.UpdateMultiballPosition(e.Guid, e.PosX, e.PosY, e.VelX, e.VelY, e.Timestamp);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"MultiballPosition handler failed: {ex.Message}");
        }
    }
}
