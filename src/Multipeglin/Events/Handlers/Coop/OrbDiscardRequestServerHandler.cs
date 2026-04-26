using BepInEx.Logging;
using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// OrbDiscardRequest is a client-to-host message. The host processes it locally
/// (via the client handler) but does NOT rebroadcast to other clients.
/// The discard result is communicated through normal OrbDiscardedEvent.
/// </summary>
public sealed class OrbDiscardRequestServerHandler : IServerHandler<OrbDiscardRequestEvent>
{
    private static readonly ManualLogSource _log = Logger.CreateLogSource("OrbDiscardRequestServer");

    public OrbDiscardRequestEvent Handle(OrbDiscardRequestEvent networkEvent)
    {
        _log.LogInfo("[OrbDiscardRequestServer] Received discard request — suppressing rebroadcast");
        return null;
    }
}
