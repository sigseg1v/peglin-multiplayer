using System;
using PeglinMods.Multiplayer.Events.Handlers.Coop;
using PeglinMods.Multiplayer.Events.Network.Coop;
using PeglinMods.Multiplayer.Events.Network.Scenarios;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;

namespace PeglinMods.Multiplayer.Events.Handlers.Scenarios;

/// <summary>
/// Client handler for PegMinigameCompleteEvent: only the host processes this.
/// Updates the client's CoopPlayerState with the chosen orb/relic reward.
/// </summary>
public sealed class PegMinigameCompleteClientHandler : IClientHandler<PegMinigameCompleteEvent>
{
    public void Handle(PegMinigameCompleteEvent e)
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
                MultiplayerPlugin.Logger?.LogWarning($"[PegMinigameComplete] From unknown peer {senderPeerId}");
                return;
            }

            if (!services.TryResolve<CoopStateManager>(out var coopState)) return;
            var playerState = coopState.GetPlayerState(slot.SlotIndex);
            if (playerState == null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[PegMinigameComplete] No CoopPlayerState for slot {slot.SlotIndex}");
                return;
            }

            if (e.Skipped)
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[PegMinigameComplete] Player '{slot.PlayerName}' (slot {slot.SlotIndex}) skipped PegMinigame reward");
            }
            else if (!string.IsNullOrEmpty(e.ChosenOrbPrefabName))
            {
                // Add orb to player state
                playerState.CompleteDeck.Add(new SerializedOrb
                {
                    PrefabName = e.ChosenOrbPrefabName,
                    Guid = Guid.NewGuid().ToString(),
                    Level = e.OrbLevel,
                });
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[PegMinigameComplete] Player '{slot.PlayerName}' (slot {slot.SlotIndex}) chose orb '{e.ChosenOrbPrefabName}' (lvl={e.OrbLevel})");
            }
            else if (e.ChosenRelicEffect >= 0)
            {
                // Add relic to player state
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
                            $"[PegMinigameComplete] Player '{slot.PlayerName}' (slot {slot.SlotIndex}) chose relic '{relic.locKey}'");
                        break;
                    }
                }
            }

            // Track completion
            CoopRewardState.ClientPegMinigameChoicesReceived.Add(slot.SlotIndex);

            MultiplayerPlugin.Logger?.LogInfo(
                $"[PegMinigameComplete] Completion: {CoopRewardState.ClientPegMinigameChoicesReceived.Count}/{CoopRewardState.TotalPegMinigameClientsExpected}" +
                $" hostDone={CoopRewardState.HostPegMinigameDone}");

            // Check if all done
            if (CoopRewardState.HostPegMinigameDone && CoopRewardState.AllClientPegMinigameChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[PegMinigameComplete] All players finished PegMinigame — proceeding");
                CoopRewardState.WaitingForOtherPlayers = false;
                CoopRewardState.PegMinigamePhaseActive = false;

                if (services.TryResolve<IGameEventRegistry>(out var evtReg))
                    evtReg.Dispatch(new AllChoicesCompleteEvent { Phase = "peg_minigame" });

                // Resume host's blocked navigation
                var pendingMgr = CoopRewardState.PendingPegMinigameManager;
                if (pendingMgr != null)
                {
                    CoopRewardState.PendingPegMinigameManager = null;
                    MultiplayerPlugin.Logger?.LogInfo("[PegMinigameComplete] Resuming host PegMinigameManager navigation");
                    pendingMgr.FadeAndLoad();
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[PegMinigameComplete] Handler failed: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
