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

        // Host: do NOT tear down the session in response to a client packet.
        // Why: a malicious client could otherwise kill the lobby for everyone with
        // a single forged DisconnectEvent. The transport's OnPeerDisconnected path
        // (ServiceRegistration) already removes the leaving peer cleanly when the
        // client's transport actually drops. Host-initiated session teardown still
        // works because it goes through the local server dispatcher, not here.
        var services = MultiplayerPlugin.Services;
        if (services != null
            && services.TryResolve<IMultiplayerMode>(out var mode)
            && mode.IsHosting)
        {
            return;
        }

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
