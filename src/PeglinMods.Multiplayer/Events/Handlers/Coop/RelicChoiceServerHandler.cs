using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Server handler for RelicChoiceEvent (client -> host).
/// Suppresses rebroadcast; the host processes the choice directly.
/// </summary>
public sealed class RelicChoiceServerHandler : IServerHandler<RelicChoiceEvent>
{
    public RelicChoiceEvent Handle(RelicChoiceEvent networkEvent) => null;
}
