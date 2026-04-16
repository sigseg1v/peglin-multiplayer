using Multipeglin.Events.Network;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers;

/// <summary>
/// Handles a DisconnectEvent received from the remote peer.
/// Triggers a full disconnect and return to main menu.
/// </summary>
public sealed class DisconnectClientHandler : IClientHandler<DisconnectEvent>
{
    public void Handle(DisconnectEvent networkEvent)
    {
        var log = MultiplayerPlugin.Logger;
        log?.LogInfo($"[Disconnect] Received disconnect from remote: {networkEvent.Reason ?? "no reason"}");

        // Use the main thread dispatcher to run the cleanup on the main thread
        // (network callbacks may fire from the poll thread)
        var dispatcher = Utility.MainThreadDispatcher.Instance;
        if (dispatcher != null)
        {
            dispatcher.Enqueue(() => MultiplayerSession.DisconnectAndReset("Remote disconnected"));
        }
        else
        {
            // Fallback: run directly (may be on main thread already)
            MultiplayerSession.DisconnectAndReset("Remote disconnected");
        }
    }
}
