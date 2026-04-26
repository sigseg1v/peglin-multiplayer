using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client-side: stores the host's per-slot orb-reward list when it targets
/// our local slot. The PopulateSuggestionOrbs patch reads from
/// CoopRewardState.PerSlotOrbChoices when it runs.
/// </summary>
public sealed class CoopOrbRewardChoicesClientHandler : IClientHandler<CoopOrbRewardChoicesEvent>
{
    public void Handle(CoopOrbRewardChoicesEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null)
            return;

        // Host→client event: host must never apply this to itself, and a forged
        // copy from another peer would otherwise poison this client's reward UI.
        if (services.TryResolve<IMultiplayerMode>(out var mode) && mode.IsHosting)
            return;

        int mySlot = CoopSlotHelper.GetLocalSlotIndex(services);
        if (mySlot < 0)
            return;
        if (networkEvent.TargetSlotIndex != mySlot)
            return;

        CoopRewardState.PerSlotOrbChoices[mySlot] = networkEvent.OrbPrefabNames ?? new System.Collections.Generic.List<string>();
        MultiplayerPlugin.Logger?.LogInfo(
            $"[CoopOrbReward] Stored {CoopRewardState.PerSlotOrbChoices[mySlot].Count} orb choices for slot {mySlot}");
    }
}
