using Multipeglin.Events.Network.Scenarios;

namespace Multipeglin.Events.Handlers.Scenarios;

/// <summary>
/// Server handler for MirrorEventCompleteEvent (client → host).
/// Suppresses rebroadcast — the host processes results directly.
/// </summary>
public sealed class MirrorEventCompleteServerHandler : IServerHandler<MirrorEventCompleteEvent>
{
    public MirrorEventCompleteEvent Handle(MirrorEventCompleteEvent networkEvent)
    {
        MultiplayerPlugin.Logger?.LogInfo("[MirrorEventCompleteServer] Received — suppressing rebroadcast");
        return null;
    }
}
