using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Server handler for RewardChoiceEvent (client -> host).
/// Suppresses rebroadcast; the host processes the choice directly.
/// </summary>
public sealed class RewardChoiceServerHandler : IServerHandler<RewardChoiceEvent>
{
    public RewardChoiceEvent Handle(RewardChoiceEvent networkEvent) => null;
}
