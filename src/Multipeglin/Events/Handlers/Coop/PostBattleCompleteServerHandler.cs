using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Server handler for PostBattleCompleteEvent (client → host).
/// Suppresses rebroadcast — the host processes results directly.
/// </summary>
public sealed class PostBattleCompleteServerHandler : IServerHandler<PostBattleCompleteEvent>
{
    public PostBattleCompleteEvent Handle(PostBattleCompleteEvent networkEvent)
    {
        MultiplayerPlugin.Logger?.LogInfo("[PostBattleCompleteServer] Received — suppressing rebroadcast");
        return null;
    }
}
