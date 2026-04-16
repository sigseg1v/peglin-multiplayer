namespace Multipeglin.Events.Handlers.Coop;

using BepInEx.Logging;
using Multipeglin.Events.Network.Coop;

/// <summary>
/// ShootRequest is a client-to-host message. The host processes it locally
/// (via the client handler) but does NOT rebroadcast to other clients.
/// The shot result will be communicated through normal ShotFired/BallPosition events.
/// </summary>
public sealed class ShootRequestServerHandler : IServerHandler<ShootRequestEvent>
{
    private static readonly ManualLogSource _log = Logger.CreateLogSource("ShootRequestServer");

    public ShootRequestEvent Handle(ShootRequestEvent networkEvent)
    {
        _log.LogInfo($"[ShootRequestServer] Received shoot request: aim=({networkEvent.AimDirectionX:F2},{networkEvent.AimDirectionY:F2}) — suppressing rebroadcast");
        return null;
    }
}
