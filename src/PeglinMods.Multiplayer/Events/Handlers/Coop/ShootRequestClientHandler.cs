namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

using PeglinMods.Multiplayer.Events.Network.Coop;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;

/// <summary>
/// On host: receives a ShootRequest from a client, validates it's their turn,
/// and queues the shot for execution by the game.
///
/// On client: this handler is a no-op (clients never receive ShootRequest from
/// the host since the server handler returns null).
/// </summary>
public sealed class ShootRequestClientHandler : IClientHandler<ShootRequestEvent>
{
    /// <summary>
    /// When the host receives a valid shoot request, it stores the pending shot here.
    /// The battle integration (future patch) reads and clears this to fire the shot.
    /// </summary>
    public static PendingShot LatestPendingShot { get; private set; }

    public void Handle(ShootRequestEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null) return;

        // Only the host processes shoot requests
        if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting) return;
        if (!services.TryResolve<TurnManager>(out var turnManager)) return;
        if (!services.TryResolve<PlayerRegistry>(out var registry)) return;
        if (!services.TryResolve<IGameEventRegistry>(out var eventRegistry)) return;

        var senderPeerId = (eventRegistry as GameEventRegistry)?.CurrentSenderPeerId ?? -1;
        var senderSlot = registry.GetSlotByPeerId(senderPeerId);
        if (senderSlot == null)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ShootRequest] No slot for peer {senderPeerId}");
            return;
        }

        // Validate it's actually this player's turn
        if (!turnManager.IsSlotsTurn(senderSlot.SlotIndex))
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[ShootRequest] Rejected shot from slot {senderSlot.SlotIndex} ({senderSlot.PlayerName}): " +
                $"not their turn (current: slot {turnManager.CurrentPlayerSlot}, phase: {turnManager.Phase})");
            return;
        }

        LatestPendingShot = new PendingShot
        {
            SlotIndex = senderSlot.SlotIndex,
            PlayerName = senderSlot.PlayerName,
            AimDirectionX = networkEvent.AimDirectionX,
            AimDirectionY = networkEvent.AimDirectionY,
            TargetEnemyGuid = networkEvent.TargetEnemyGuid,
        };

        MultiplayerPlugin.Logger?.LogInfo(
            $"[ShootRequest] Accepted shot from slot {senderSlot.SlotIndex} ({senderSlot.PlayerName}): " +
            $"dir=({networkEvent.AimDirectionX:F3}, {networkEvent.AimDirectionY:F3})");
    }

    /// <summary>
    /// Peek at the pending shot without clearing it.
    /// Returns the shot or null if none pending.
    /// </summary>
    public static PendingShot PeekPendingShot()
    {
        return LatestPendingShot;
    }

    /// <summary>
    /// Consume the pending shot (called by the battle integration patch).
    /// Returns the shot and clears it, or null if none pending.
    /// </summary>
    public static PendingShot ConsumePendingShot()
    {
        var shot = LatestPendingShot;
        LatestPendingShot = null;
        return shot;
    }
}

/// <summary>
/// Data class holding a validated shoot request waiting for execution.
/// </summary>
public class PendingShot
{
    public int SlotIndex { get; set; }
    public string PlayerName { get; set; }
    public float AimDirectionX { get; set; }
    public float AimDirectionY { get; set; }
    public string TargetEnemyGuid { get; set; }
}
