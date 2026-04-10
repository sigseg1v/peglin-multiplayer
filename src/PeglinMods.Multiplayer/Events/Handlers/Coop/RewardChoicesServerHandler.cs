using BepInEx.Logging;
using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Server handler for RewardChoicesEvent (host -> targeted client).
/// Passes through for broadcast.
/// </summary>
public sealed class RewardChoicesServerHandler : IServerHandler<RewardChoicesEvent>
{
    private static readonly ManualLogSource _log = Logger.CreateLogSource("RewardChoicesServer");

    public RewardChoicesEvent Handle(RewardChoicesEvent networkEvent)
    {
        _log.LogInfo($"[RewardChoicesServer] Broadcasting reward choices to slot {networkEvent.TargetSlotIndex}: {networkEvent.Options?.Count ?? 0} options");
        return networkEvent;
    }
}
