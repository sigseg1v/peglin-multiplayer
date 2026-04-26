using System;
using System.Linq;
using Multipeglin.Events.Network.Coop;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client handler for RewardChoiceEvent: only the host processes this
/// (a client's reward selection arriving at the host).
/// Looks up the reward options that were sent, applies the chosen reward
/// to the player's CoopPlayerState, and tracks completion.
/// </summary>
public sealed class RewardChoiceClientHandler : IClientHandler<RewardChoiceEvent>
{
    public void Handle(RewardChoiceEvent networkEvent)
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
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopReward] RewardChoice from unknown peer {senderPeerId}");
                return;
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopReward] Player '{slot.PlayerName}' (slot {slot.SlotIndex}) chose reward option {networkEvent.ChosenOptionIndex}");

            // Look up the reward options that were sent to this slot
            if (!CoopRewardState.PendingSentRewardChoices.TryGetValue(slot.SlotIndex, out var sentChoices))
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopReward] No pending reward choices found for slot {slot.SlotIndex} -- ignoring");
                return;
            }

            // Find the chosen option by index
            var chosenOption = sentChoices.Options.FirstOrDefault(o => o.OptionIndex == networkEvent.ChosenOptionIndex);
            if (chosenOption == null)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopReward] Invalid option index {networkEvent.ChosenOptionIndex} for slot {slot.SlotIndex} " +
                    $"(available: {string.Join(", ", sentChoices.Options.Select(o => o.OptionIndex))})");
                return;
            }

            // Apply the reward to the player's CoopPlayerState
            if (!services.TryResolve<CoopStateManager>(out var coopState))
            {
                MultiplayerPlugin.Logger?.LogWarning("[CoopReward] CoopStateManager not available -- cannot apply reward");
                return;
            }

            var playerState = coopState.GetPlayerState(slot.SlotIndex);
            if (playerState == null)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopReward] No CoopPlayerState for slot {slot.SlotIndex} -- cannot apply reward");
                return;
            }

            ApplyReward(playerState, chosenOption, slot.SlotIndex);

            // Track this client's choice
            CoopRewardState.ClientRewardChoicesReceived.Add(slot.SlotIndex);

            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopReward] Reward choices received: {CoopRewardState.ClientRewardChoicesReceived.Count}/{CoopRewardState.TotalRewardClientsExpected}");

            // Check if all players have now chosen
            if (CoopRewardState.AllClientRewardChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[CoopReward] All post-battle reward choices received");

                // Clean up pending state
                CoopRewardState.PendingSentRewardChoices.Clear();
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] RewardChoice handler failed: {e.Message}");
        }
    }

    private static void ApplyReward(CoopPlayerState playerState, RewardOption option, int slotIndex)
    {
        switch (option.Type)
        {
            case "heal":
                {
                    float before = playerState.CurrentHealth;
                    float maxHp = playerState.MaxHealth;
                    playerState.CurrentHealth = Math.Min(playerState.CurrentHealth + 20f, maxHp);
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[CoopReward] Slot {slotIndex} heal: HP {before} -> {playerState.CurrentHealth} (max {maxHp})");
                    break;
                }

            case "max_hp":
                {
                    float beforeMax = playerState.MaxHealth;
                    float beforeCur = playerState.CurrentHealth;
                    playerState.MaxHealth += 5f;
                    playerState.CurrentHealth += 5f;
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[CoopReward] Slot {slotIndex} max_hp: MaxHP {beforeMax} -> {playerState.MaxHealth}, " +
                        $"HP {beforeCur} -> {playerState.CurrentHealth}");
                    break;
                }

            case "skip":
                {
                    int beforeGold = playerState.Gold;
                    int goldReward = option.GoldReward > 0 ? option.GoldReward : 10;
                    playerState.Gold += goldReward;
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[CoopReward] Slot {slotIndex} skip: Gold {beforeGold} -> {playerState.Gold} (+{goldReward})");
                    break;
                }

            default:
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopReward] Slot {slotIndex} unknown reward type '{option.Type}' -- not applied");
                break;
        }
    }
}
