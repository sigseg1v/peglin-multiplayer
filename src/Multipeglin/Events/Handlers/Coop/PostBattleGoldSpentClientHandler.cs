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
            if (services == null) return;
            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting) return;

            var registry = services.TryResolve<IGameEventRegistry>(out var reg) ? reg : null;
            var senderPeerId = (registry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;

            if (!services.TryResolve<PlayerRegistry>(out var playerRegistry)) return;
            var slot = playerRegistry.GetSlotByPeerId(senderPeerId);
            if (slot == null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[PostBattleGoldSpent] From unknown peer {senderPeerId}");
                return;
            }

            if (!services.TryResolve<CoopStateManager>(out var coopState)) return;
            var playerState = coopState.GetPlayerState(slot.SlotIndex);
            if (playerState == null) return;

            int before = playerState.Gold;
            playerState.Gold = Math.Max(0, playerState.Gold - e.Amount);
            MultiplayerPlugin.Logger?.LogInfo(
                $"[PostBattleGoldSpent] Slot {slot.SlotIndex} spent {e.Amount} gold ({before} -> {playerState.Gold})");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[PostBattleGoldSpent] Handler failed: {ex.Message}");
        }
    }
}
