using System;
using Multipeglin.Events.Network.Coop;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Runs on the host when a client sends PostBattleGoldSpentEvent. Immediately
/// deducts the spent gold from the sending client's CoopPlayerState so the next
/// heartbeat doesn't bounce the client's gold back up.
/// </summary>
public sealed class PostBattleGoldSpentClientHandler : IClientHandler<PostBattleGoldSpentEvent>
{
    public void Handle(PostBattleGoldSpentEvent e)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                return;
            }

            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting)
            {
                return;
            }

            var registry = services.TryResolve<IGameEventRegistry>(out var reg) ? reg : null;
            var senderPeerId = (registry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;

            if (!services.TryResolve<PlayerRegistry>(out var playerRegistry))
            {
                return;
            }

            var slot = playerRegistry.GetSlotByPeerId(senderPeerId);
            if (slot == null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[PostBattleGoldSpent] From unknown peer {senderPeerId}");
                return;
            }

            if (!services.TryResolve<CoopStateManager>(out var coopState))
            {
                return;
            }

            var playerState = coopState.GetPlayerState(slot.SlotIndex);
            if (playerState == null)
            {
                return;
            }

            var before = playerState.Gold;
            playerState.Gold = Math.Max(0, playerState.Gold - e.Amount);

            var prevHp = playerState.CurrentHealth;
            var prevMaxHp = playerState.MaxHealth;

            // MaxHP can only increase in the post-battle reward screen (Basalt
            // Toadem / MAX_HP_INC). Never let a lower client-reported value
            // regress the host's authoritative max.
            if (e.MaxHealth > prevMaxHp)
            {
                playerState.MaxHealth = e.MaxHealth;
            }

            // CurrentHealth can only increase here (Heal button). Enforce
            // monotonicity so a stale capture (e.g. client grabbed HP before
            // HealEndOfBattleAmount applied, or the heartbeat had briefly
            // stamped another slot's value onto the local PlayerHealthController)
            // can't drop host HP. Clamp to MaxHealth.
            if (e.CurrentHealth > prevHp)
            {
                playerState.CurrentHealth = Math.Min(e.CurrentHealth, playerState.MaxHealth);
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[PostBattleGoldSpent] Slot {slot.SlotIndex} spent {e.Amount} gold ({before} -> {playerState.Gold}), " +
                $"hp {prevHp}/{prevMaxHp} -> {playerState.CurrentHealth}/{playerState.MaxHealth} (client reported {e.CurrentHealth}/{e.MaxHealth})");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[PostBattleGoldSpent] Handler failed: {ex.Message}");
        }
    }
}
