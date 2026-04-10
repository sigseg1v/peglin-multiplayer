using BepInEx.Logging;
using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Server handler for RelicChoicesEvent (host -> targeted client).
/// Passes through for broadcast.
/// </summary>
public sealed class RelicChoicesServerHandler : IServerHandler<RelicChoicesEvent>
{
    private static readonly ManualLogSource _log = Logger.CreateLogSource("RelicChoicesServer");

    public RelicChoicesEvent Handle(RelicChoicesEvent networkEvent)
    {
        _log.LogInfo($"[RelicChoicesServer] Broadcasting relic choices to slot {networkEvent.TargetSlotIndex}: {networkEvent.Choices?.Count ?? 0} options");
        return networkEvent;
    }
}
