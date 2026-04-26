using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client-side: stores the host's per-slot treasure relic choice when it targets
/// our local slot. The BattleUpgradeCanvas.SetupRelicGrant patch reads from
/// CoopRewardState.PerSlotTreasureRelics when it runs.
/// </summary>
public sealed class CoopTreasureRelicChoiceClientHandler : IClientHandler<CoopTreasureRelicChoiceEvent>
{
    public void Handle(CoopTreasureRelicChoiceEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null) return;

        int mySlot = CoopSlotHelper.GetLocalSlotIndex(services);
        if (mySlot < 0) return;
        if (networkEvent.TargetSlotIndex != mySlot) return;

        CoopRewardState.PerSlotTreasureRelics[mySlot] = networkEvent.RelicName;
        MultiplayerPlugin.Logger?.LogInfo(
            $"[CoopTreasureRelic] Stored host-rolled relic '{networkEvent.RelicName}' (rarity={networkEvent.Rarity}) for slot {mySlot}");
    }
}
