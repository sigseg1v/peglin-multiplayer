using Multipeglin.Events.Network.Coop;
using Multipeglin.UI;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Renders the pending damage overlay on both host and client.
/// On the host this runs locally via Dispatch; on the client it runs
/// when the event arrives over the network.
/// </summary>
public sealed class PendingDamagePreviewClientHandler : IClientHandler<PendingDamagePreviewEvent>
{
    public void Handle(PendingDamagePreviewEvent networkEvent)
    {
        if (networkEvent.Entries == null || networkEvent.Entries.Count == 0)
        {
            PendingDamageOverlay.ClearAll();
            return;
        }

        foreach (var entry in networkEvent.Entries)
        {
            PendingDamageOverlay.SetPlayerDamage(
                entry.SlotIndex,
                entry.PlayerName,
                entry.Damage,
                entry.TargetEnemyGuid,
                entry.IsAoE);
        }
    }
}
