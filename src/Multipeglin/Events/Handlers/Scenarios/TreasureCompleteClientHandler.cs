using System;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Events.Network.Scenarios;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Scenarios;

/// <summary>
/// Client handler for TreasureCompleteEvent: only the host processes this.
/// Updates the client's CoopPlayerState with the chosen relic.
/// </summary>
public sealed class TreasureCompleteClientHandler : IClientHandler<TreasureCompleteEvent>
{
    public void Handle(TreasureCompleteEvent e)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null)
                return;
            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting)
                return;

            var eventRegistry = services.TryResolve<IGameEventRegistry>(out var reg) ? reg : null;
            var senderPeerId = (eventRegistry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;

            if (!services.TryResolve<PlayerRegistry>(out var registry))
                return;
            var slot = registry.GetSlotByPeerId(senderPeerId);
            if (slot == null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[TreasureComplete] From unknown peer {senderPeerId}");
                return;
            }

            if (e.ChosenRelicEffect >= 0)
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[TreasureComplete] Player '{slot.PlayerName}' (slot {slot.SlotIndex}) chose relic: " +
                    $"effect={e.ChosenRelicEffect}, name='{e.ChosenRelicName}'");

                if (services.TryResolve<CoopStateManager>(out var coopState))
                {
                    var playerState = coopState.GetPlayerState(slot.SlotIndex);
                    if (playerState != null)
                    {
                        // Find the Relic asset and add to player state
                        var allRelics = Resources.FindObjectsOfTypeAll<Relics.Relic>();
                        foreach (var relic in allRelics)
                        {
                            if ((int)relic.effect == e.ChosenRelicEffect)
                            {
                                playerState.OwnedRelics.Add(new SerializedRelic
                                {
                                    Effect = e.ChosenRelicEffect,
                                    LocKey = relic.locKey,
                                    Rarity = (int)relic.globalRarity,
                                });
                                MultiplayerPlugin.Logger?.LogInfo(
                                    $"[TreasureComplete] Added relic '{relic.locKey}' to slot {slot.SlotIndex}");
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[TreasureComplete] Player '{slot.PlayerName}' (slot {slot.SlotIndex}) skipped treasure relic");
            }

            // Track completion
            CoopRewardState.ClientTreasureChoicesReceived.Add(slot.SlotIndex);

            // Check if all done
            if (CoopRewardState.HostTreasureDone && CoopRewardState.AllClientTreasureChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[TreasureComplete] All players finished treasure — proceeding");
                CoopRewardState.WaitingForOtherPlayers = false;
                CoopRewardState.TreasurePhaseActive = false;

                if (services.TryResolve<IGameEventRegistry>(out var evtReg))
                    evtReg.Dispatch(new AllChoicesCompleteEvent { Phase = "treasure" });

                // Resume host's blocked Skip
                var pendingChest = CoopRewardState.PendingChestController;
                if (pendingChest != null)
                {
                    CoopRewardState.PendingChestController = null;
                    MultiplayerPlugin.Logger?.LogInfo("[TreasureComplete] Resuming host ChestScenarioController.Skip");
                    pendingChest.Skip();
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[TreasureComplete] Handler failed: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
