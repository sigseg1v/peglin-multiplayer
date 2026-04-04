namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

using PeglinMods.Multiplayer.Events.Network.Coop;

/// <summary>
/// ShootRequest is a client-to-host message. The host processes it locally
/// (via the client handler) but does NOT rebroadcast to other clients.
/// The shot result will be communicated through normal ShotFired/BallPosition events.
/// </summary>
public sealed class ShootRequestServerHandler : IServerHandler<ShootRequestEvent>
{
    public ShootRequestEvent Handle(ShootRequestEvent networkEvent) => null;
}
