using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Server handler for PostBattleRelicChoicesEvent: suppress rebroadcast.
/// The host dispatches this locally (which triggers the server handler to
/// serialize and send to clients). No need to rebroadcast.
/// </summary>
public sealed class PostBattleRelicChoicesServerHandler : IServerHandler<PostBattleRelicChoicesEvent>
{
    public PostBattleRelicChoicesEvent Handle(PostBattleRelicChoicesEvent e) => e;
}
