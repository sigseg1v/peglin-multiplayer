using System.Collections.Generic;

namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Sent from host to clients on every peg hit during coop battles.
/// Contains each player's running damage total and target, so the client
/// can render the same persistent damage overlay as the host.
/// An empty Entries list signals "clear overlay".
/// </summary>
public class PendingDamagePreviewEvent
{
    public List<DamageEntry> Entries { get; set; } = new();

    public class DamageEntry
    {
        public int SlotIndex { get; set; }

        public string PlayerName { get; set; }

        public long Damage { get; set; }

        public string TargetEnemyGuid { get; set; }

        public bool IsAoE { get; set; }
    }
}
