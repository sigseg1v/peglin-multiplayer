namespace Multipeglin.Events.Handlers.Ball;

using System;
using Multipeglin.Events.Network.Ball;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;

public sealed class MultiballDestroyedClientHandler : IClientHandler<MultiballDestroyedEvent>
{
    public void Handle(MultiballDestroyedEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            ClientBallRenderer.Instance?.OnMultiballDestroyed(e.Guid);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"MultiballDestroyed handler failed: {ex.Message}");
        }
    }
}
