using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Server handler for PostBattleStartEvent (host → clients).
/// Passes through for broadcast to all clients.
/// </summary>
public sealed class PostBattleStartServerHandler : IServerHandler<PostBattleStartEvent>
{
    public PostBattleStartEvent Handle(PostBattleStartEvent networkEvent)
    {
        MultiplayerPlugin.Logger?.LogInfo("[PostBattleStartServer] Broadcasting post-battle start to clients");
        return networkEvent;
    }
}
