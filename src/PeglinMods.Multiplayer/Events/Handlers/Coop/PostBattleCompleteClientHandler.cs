using System;
using System.Linq;
using PeglinMods.Multiplayer.Events.Network.Coop;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Client handler for PostBattleCompleteEvent: only the host processes this.
/// A client's post-battle reward results arrive at the host. The host updates
/// CoopPlayerState and tracks completion. When all players are done, dispatches
/// AllChoicesCompleteEvent and triggers navigation.
/// </summary>
public sealed class PostBattleCompleteClientHandler : IClientHandler<PostBattleCompleteEvent>
{
    public void Handle(PostBattleCompleteEvent networkEvent)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null) return;

            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting) return;

            // Identify the sending client by peer ID
            var eventRegistry = services.TryResolve<IGameEventRegistry>(out var reg) ? reg : null;
            var senderPeerId = (eventRegistry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;

            if (!services.TryResolve<PlayerRegistry>(out var registry)) return;
            var slot = registry.GetSlotByPeerId(senderPeerId);
            if (slot == null)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[PostBattleComplete] From unknown peer {senderPeerId}");
                return;
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[PostBattleComplete] Player '{slot.PlayerName}' (slot {slot.SlotIndex}) finished rewards: " +
                $"HP={networkEvent.CurrentHealth}/{networkEvent.MaxHealth} Gold={networkEvent.Gold} Deck={networkEvent.CompleteDeck?.Count ?? 0}");

            // Update CoopPlayerState with the client's post-reward state
            if (!services.TryResolve<CoopStateManager>(out var coopState)) return;
            var playerState = coopState.GetPlayerState(slot.SlotIndex);
            if (playerState == null)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[PostBattleComplete] No CoopPlayerState for slot {slot.SlotIndex}");
                return;
            }

            playerState.CurrentHealth = networkEvent.CurrentHealth;
            playerState.MaxHealth = networkEvent.MaxHealth;
            playerState.Gold = networkEvent.Gold;

            // Replace the complete deck with the client's updated deck
            if (networkEvent.CompleteDeck != null)
            {
                playerState.CompleteDeck = networkEvent.CompleteDeck.Select(o => new SerializedOrb
                {
                    PrefabName = o.PrefabName,
                    Guid = Guid.NewGuid().ToString(), // assign fresh GUIDs for the new deck state
                    Level = o.Level,
                }).ToList();
            }

            // Battle deck and shuffled order will be rebuilt at the start of the next battle
            playerState.BattleDeck.Clear();
            playerState.ShuffledOrder.Clear();
            playerState.CurrentOrb = "";

            // Add the chosen boss/rare relic to the player's relic list
            if (networkEvent.ChosenRelicEffect >= 0)
            {
                var allRelics = Resources.FindObjectsOfTypeAll<Relics.Relic>();
                Relics.Relic chosenRelic = null;
                foreach (var r in allRelics)
                {
                    if ((int)r.effect == networkEvent.ChosenRelicEffect)
                    {
                        chosenRelic = r;
                        break;
                    }
                }

                if (chosenRelic != null)
                {
                    playerState.OwnedRelics.Add(new SerializedRelic
                    {
                        Effect = networkEvent.ChosenRelicEffect,
                        LocKey = chosenRelic.locKey,
                        Rarity = (int)chosenRelic.globalRarity,
                    });
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[PostBattleComplete] Added relic '{chosenRelic.locKey}' (effect={networkEvent.ChosenRelicEffect}) to slot {slot.SlotIndex}");
                }
                else
                {
                    MultiplayerPlugin.Logger?.LogWarning(
                        $"[PostBattleComplete] Could not find Relic asset for effect={networkEvent.ChosenRelicEffect}");
                }
            }
            else if (!string.IsNullOrEmpty(networkEvent.ChosenRelicName))
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[PostBattleComplete] Player skipped boss relic selection");
            }

            // Track completion
            CoopRewardState.ClientRewardChoicesReceived.Add(slot.SlotIndex);

            MultiplayerPlugin.Logger?.LogInfo(
                $"[PostBattleComplete] Reward completion: {CoopRewardState.ClientRewardChoicesReceived.Count}/{CoopRewardState.TotalRewardClientsExpected}" +
                $" hostDone={CoopRewardState.HostRewardsDone}");

            // Check if all players are done
            if (CoopRewardState.HostRewardsDone && CoopRewardState.AllClientRewardChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[PostBattleComplete] All players done — dispatching AllChoicesComplete and starting navigation");

                CoopRewardState.HostRewardPhaseActive = false;
                CoopRewardState.WaitingForOtherPlayers = false;

                if (services.TryResolve<IGameEventRegistry>(out var evtReg))
                    evtReg.Dispatch(new AllChoicesCompleteEvent { Phase = "post_battle" });

                // Call the stored PostBattleController.StartNavigation on the host
                var pbc = CoopRewardState.PendingPostBattleController;
                if (pbc != null)
                {
                    CoopRewardState.PendingPostBattleController = null;
                    // Use reflection to call the private StartNavigation method
                    var navMethod = HarmonyLib.AccessTools.Method(typeof(global::Battle.PostBattleController), "StartNavigation");
                    navMethod?.Invoke(pbc, new object[] { true });
                }
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[PostBattleComplete] Handler failed: {e.Message}");
        }
    }
}
