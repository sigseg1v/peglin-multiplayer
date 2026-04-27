using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Passthrough — host dispatches this from the post-battle patch and we want
/// it broadcast verbatim to all clients.
/// </summary>
public sealed class PostBattleBossHealServerHandler : IServerHandler<PostBattleBossHealEvent>
{
    public PostBattleBossHealEvent Handle(PostBattleBossHealEvent networkEvent) => networkEvent;
}
