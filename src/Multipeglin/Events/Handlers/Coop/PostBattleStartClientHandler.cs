using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client handler for PostBattleStartEvent: activates the native PostBattleController
/// so the client gets the same reward screen as the host.
/// </summary>
public sealed class PostBattleStartClientHandler : IClientHandler<PostBattleStartEvent>
{
    public void Handle(PostBattleStartEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null) return;

        // Only process on client (spectating)
        if (services.TryResolve<IMultiplayerMode>(out var mode) && mode.IsHosting)
            return;

        MultiplayerPlugin.Logger?.LogInfo("[PostBattleStart] Client received post-battle start — activating native reward screen");

        // Set flags BEFORE activating the controller
        CoopRewardState.ClientInNativeRewardPhase = true;
        CoopRewardState.AllChoicesComplete = false;
        CoopRewardState.WaitingForOtherPlayers = false;
        CoopRewardState.PendingPostBattleRelicChoices = null;

        // Enable method bypass so the native reward screen can modify game state
        Patches.MultiplayerClientPatches.AllowNativeRewardLogic = true;
        Patches.MultiplayerClientPatches.ClientChosenPostBattleRelicEffect = -1;
        Patches.MultiplayerClientPatches.ClientChosenPostBattleRelicName = null;

        // Find PostBattleController — it's on a disabled GameObject in the Battle scene
        var pbcs = Resources.FindObjectsOfTypeAll<global::Battle.PostBattleController>();
        if (pbcs == null || pbcs.Length == 0)
        {
            MultiplayerPlugin.Logger?.LogWarning("[PostBattleStart] No PostBattleController found in scene!");
            CoopRewardState.ClientInNativeRewardPhase = false;
            Patches.MultiplayerClientPatches.AllowNativeRewardLogic = false;
            return;
        }

        var pbc = pbcs[0];

        // Activate it — this triggers OnEnable() which calls
        // BattleUpgradeCanvas.ConfigureForPostBattleRewards() with the
        // client's own singletons (already populated via heartbeat sync)
        pbc.gameObject.SetActive(true);

        MultiplayerPlugin.Logger?.LogInfo("[PostBattleStart] PostBattleController activated on client");
    }
}
