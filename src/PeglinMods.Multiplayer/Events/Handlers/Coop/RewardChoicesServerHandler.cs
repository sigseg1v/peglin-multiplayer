using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Server handler for RewardChoicesEvent (host -> targeted client).
/// Passes through for broadcast.
/// </summary>
public sealed class RewardChoicesServerHandler : IServerHandler<RewardChoicesEvent>
{
    public RewardChoicesEvent Handle(RewardChoicesEvent networkEvent) => networkEvent;
}
