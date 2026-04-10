using BepInEx.Logging;
using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Server handler for RelicChoiceEvent (client -> host).
/// Suppresses rebroadcast; the host processes the choice directly.
/// </summary>
public sealed class RelicChoiceServerHandler : IServerHandler<RelicChoiceEvent>
{
    private static readonly ManualLogSource _log = Logger.CreateLogSource("RelicChoiceServer");

    public RelicChoiceEvent Handle(RelicChoiceEvent networkEvent)
    {
        _log.LogInfo($"[RelicChoiceServer] Received relic choice: effect={networkEvent.ChosenRelicEffect} — suppressing rebroadcast");
        return null;
    }
}
