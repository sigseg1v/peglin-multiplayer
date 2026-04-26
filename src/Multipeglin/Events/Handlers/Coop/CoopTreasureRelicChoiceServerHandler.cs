using BepInEx.Logging;
using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Host-side: passes the per-slot treasure relic choice through for broadcast
/// to the targeted client. The host authoritatively rolls each slot's relic
/// in the SetupRelicGrant Postfix patch.
/// </summary>
public sealed class CoopTreasureRelicChoiceServerHandler : IServerHandler<CoopTreasureRelicChoiceEvent>
{
    private static readonly ManualLogSource _log = Logger.CreateLogSource("CoopTreasureRelic");

    public CoopTreasureRelicChoiceEvent Handle(CoopTreasureRelicChoiceEvent networkEvent)
    {
        _log.LogInfo($"[CoopTreasureRelic] Broadcasting relic '{networkEvent.RelicName}' (rarity={networkEvent.Rarity}) to slot {networkEvent.TargetSlotIndex}");
        return networkEvent;
    }
}
