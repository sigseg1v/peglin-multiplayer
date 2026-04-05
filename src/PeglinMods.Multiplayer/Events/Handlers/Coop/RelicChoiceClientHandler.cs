using System;
using PeglinMods.Multiplayer.Events.Network.Coop;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Client handler for RelicChoiceEvent: only the host processes this
/// (a client's relic selection arriving at the host).
/// Applies the relic to the player's CoopPlayerState and checks if all
/// players have finished choosing so the game can proceed to the map.
/// </summary>
public sealed class RelicChoiceClientHandler : IClientHandler<RelicChoiceEvent>
{
    public void Handle(RelicChoiceEvent networkEvent)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null) return;

            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting) return;

            var eventRegistry = services.TryResolve<IGameEventRegistry>(out var reg) ? reg : null;
            var senderPeerId = (eventRegistry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;

            if (!services.TryResolve<PlayerRegistry>(out var registry)) return;
            var slot = registry.GetSlotByPeerId(senderPeerId);
            if (slot == null)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopReward] RelicChoice from unknown peer {senderPeerId}");
                return;
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopReward] Player '{slot.PlayerName}' (slot {slot.SlotIndex}) chose relic effect {networkEvent.ChosenRelicEffect}");

            // Apply the relic to the player's CoopPlayerState
            if (services.TryResolve<CoopStateManager>(out var coopState))
            {
                var playerState = coopState.GetPlayerState(slot.SlotIndex);
                if (playerState != null)
                {
                    // Find the relic data to get display info
                    var relicMgrs = Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                    string locKey = "";
                    int rarity = 0;
                    if (relicMgrs != null && relicMgrs.Length > 0)
                    {
                        foreach (var r in relicMgrs[0].CommonRelicPool)
                        {
                            if ((int)r.effect == networkEvent.ChosenRelicEffect)
                            { locKey = r.locKey; rarity = (int)r.globalRarity; break; }
                        }
                    }

                    playerState.OwnedRelics.Add(new SerializedRelic
                    {
                        Effect = networkEvent.ChosenRelicEffect,
                        LocKey = locKey,
                        Rarity = rarity,
                    });
                    MultiplayerPlugin.Logger?.LogInfo($"[CoopReward] Added relic {networkEvent.ChosenRelicEffect} to slot {slot.SlotIndex} state");
                }
            }

            // Track this client's choice
            CoopRewardState.ClientRelicChoicesReceived.Add(slot.SlotIndex);

            // Check if all players have now chosen
            if (CoopRewardState.HostHasChosenRelic && CoopRewardState.AllClientRelicChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[CoopReward] All relic choices received -- triggering map load");
                CoopRewardState.HostRelicSelectionActive = false;
                CoopRewardState.WaitingForOtherPlayers = false;

                // Dispatch AllChoicesCompleteEvent to clients
                if (services.TryResolve<IGameEventRegistry>(out var reg2))
                {
                    reg2.Dispatch(new AllChoicesCompleteEvent { Phase = "starting_relic" });
                }

                // Call LoadMapScene on the stored GameInit instance
                var gameInit = CoopRewardState.PendingGameInitInstance as GameInit;
                if (gameInit != null)
                {
                    var loadMapMethod = typeof(GameInit).GetMethod("LoadMapScene",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    loadMapMethod?.Invoke(gameInit, null);
                }
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] RelicChoice handler failed: {e.Message}");
        }
    }
}
