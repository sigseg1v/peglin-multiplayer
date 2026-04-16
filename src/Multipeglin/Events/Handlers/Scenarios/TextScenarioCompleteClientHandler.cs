using System;
using System.Linq;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Events.Network.Scenarios;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Scenarios;

/// <summary>
/// Client handler for TextScenarioCompleteEvent: only the host processes this.
/// Replaces the client's CoopPlayerState with their post-dialogue state.
/// </summary>
public sealed class TextScenarioCompleteClientHandler : IClientHandler<TextScenarioCompleteEvent>
{
    public void Handle(TextScenarioCompleteEvent e)
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
                MultiplayerPlugin.Logger?.LogWarning($"[TextScenarioComplete] From unknown peer {senderPeerId}");
                return;
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[TextScenarioComplete] Player '{slot.PlayerName}' (slot {slot.SlotIndex}): " +
                $"deck={e.CompleteDeck?.Count ?? 0}, hp={e.CurrentHealth}/{e.MaxHealth}, " +
                $"gold={e.Gold}, relics={e.Relics?.Count ?? 0}");

            if (!services.TryResolve<CoopStateManager>(out var coopState)) return;
            var playerState = coopState.GetPlayerState(slot.SlotIndex);
            if (playerState == null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[TextScenarioComplete] No CoopPlayerState for slot {slot.SlotIndex}");
                return;
            }

            // Replace deck wholesale with client's post-dialogue deck
            if (e.CompleteDeck != null)
            {
                playerState.CompleteDeck = e.CompleteDeck.ToList();
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[TextScenarioComplete] Deck updated: {string.Join(", ", playerState.CompleteDeck.Select(o => $"{o.PrefabName}(L{o.Level})"))}");
            }

            // Update health
            playerState.CurrentHealth = e.CurrentHealth;
            playerState.MaxHealth = e.MaxHealth;

            // Update gold
            playerState.Gold = e.Gold;

            // Update relics
            if (e.Relics != null)
            {
                playerState.OwnedRelics = e.Relics.ToList();
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[TextScenarioComplete] Relics updated: {string.Join(", ", playerState.OwnedRelics.Select(r => r.LocKey))}");
            }

            // Track completion for wait-for-all
            CoopRewardState.ClientTextScenarioChoicesReceived.Add(slot.SlotIndex);

            MultiplayerPlugin.Logger?.LogInfo(
                $"[TextScenarioComplete] Slot {slot.SlotIndex} done " +
                $"({CoopRewardState.ClientTextScenarioChoicesReceived.Count}/{CoopRewardState.TotalTextScenarioClientsExpected} clients)");

            // Check if all done
            if (CoopRewardState.HostTextScenarioDone && CoopRewardState.AllClientTextScenarioChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[TextScenarioComplete] All players finished TextScenario — proceeding");
                CoopRewardState.WaitingForOtherPlayers = false;
                CoopRewardState.TextScenarioPhaseActive = false;

                if (services.TryResolve<IGameEventRegistry>(out var evtReg))
                    evtReg.Dispatch(new AllChoicesCompleteEvent { Phase = "text_scenario" });

                // Resume host's blocked StartNavigation
                if (CoopRewardState.PendingDialogueSystemScenario is RNG.Scenarios.DialogueSystemScenario scenario)
                {
                    CoopRewardState.PendingDialogueSystemScenario = null;
                    MultiplayerPlugin.Logger?.LogInfo("[TextScenarioComplete] Resuming host StartNavigation");
                    // Invoke ConversationEnded which calls StartNavigation
                    scenario.ConversationEnded();
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[TextScenarioComplete] Handler failed: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
