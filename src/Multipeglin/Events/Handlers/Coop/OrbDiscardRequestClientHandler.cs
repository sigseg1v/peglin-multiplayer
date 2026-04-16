namespace Multipeglin.Events.Handlers.Coop;

using Multipeglin.Events.Network.Coop;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;

/// <summary>
/// On host: receives an OrbDiscardRequest from a client, validates it's their turn,
/// and queues the discard for execution by BattleController_Update_Postfix.
/// </summary>
public sealed class OrbDiscardRequestClientHandler : IClientHandler<OrbDiscardRequestEvent>
{
    /// <summary>
    /// When true, BattleController_Update_Postfix should call AttemptOrbDiscard on the host.
    /// </summary>
    public static bool PendingDiscard { get; set; }

    public void Handle(OrbDiscardRequestEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null) return;

        // Only the host processes discard requests
        if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting) return;
        if (!services.TryResolve<TurnManager>(out var turnManager)) return;
        if (!services.TryResolve<PlayerRegistry>(out var registry)) return;
        if (!services.TryResolve<IGameEventRegistry>(out var eventRegistry)) return;

        var senderPeerId = (eventRegistry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;
        var senderSlot = registry.GetSlotByPeerId(senderPeerId);
        if (senderSlot == null)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[OrbDiscardRequest] No slot for peer {senderPeerId}");
            return;
        }

        // Validate it's actually this player's turn
        if (!turnManager.IsSlotsTurn(senderSlot.SlotIndex))
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[OrbDiscardRequest] Rejected from slot {senderSlot.SlotIndex} ({senderSlot.PlayerName}): " +
                $"not their turn (current: slot {turnManager.CurrentPlayerSlot}, phase: {turnManager.Phase})");
            return;
        }

        MultiplayerPlugin.Logger?.LogInfo(
            $"[OrbDiscardRequest] Accepted discard from slot {senderSlot.SlotIndex} ({senderSlot.PlayerName})");

        PendingDiscard = true;
    }
}
