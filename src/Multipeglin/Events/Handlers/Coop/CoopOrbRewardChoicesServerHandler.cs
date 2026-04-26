using BepInEx.Logging;
using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Host-side: passes the per-slot orb-reward choices through for broadcast
/// to the targeted client. The host authoritatively rolls each slot's list
/// in CoopSubscriptions.OnVictory.
/// </summary>
public sealed class CoopOrbRewardChoicesServerHandler : IServerHandler<CoopOrbRewardChoicesEvent>
{
    private static readonly ManualLogSource _log = Logger.CreateLogSource("CoopOrbReward");

    public CoopOrbRewardChoicesEvent Handle(CoopOrbRewardChoicesEvent networkEvent)
    {
        _log.LogInfo($"[CoopOrbReward] Broadcasting {networkEvent.OrbPrefabNames?.Count ?? 0} orb choices to slot {networkEvent.TargetSlotIndex}");
        return networkEvent;
    }
}
