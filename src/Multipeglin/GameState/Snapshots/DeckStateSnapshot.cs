using System.Collections.Generic;

namespace Multipeglin.GameState.Snapshots;

public class DeckStateSnapshot
{
    /// <summary>
    /// In coop mode, identifies which player slot this snapshot belongs to.
    /// -1 means unspecified (single-player or legacy).
    /// </summary>
    public int ActiveSlotIndex { get; set; } = -1;

    public List<OrbEntry> CompleteDeck { get; set; } = new List<OrbEntry>();

    public List<OrbEntry> BattleDeck { get; set; } = new List<OrbEntry>();

    /// <summary>
    /// Orb identifiers in shuffledDeck stack order (top of stack = first to draw = index 0).
    /// For the active player, these are OrbIdentifier GUIDs (12-char hex strings).
    /// For non-active coop players, these are prefab names (from CoopPlayerState).
    /// The applier handles both formats.
    /// </summary>
    public List<string> ShuffledOrder { get; set; } = new List<string>();

    public string CurrentOrb { get; set; }

    /// <summary>
    /// GUID of the active orb (bc.activePachinkoBall) if it's tracked by OrbIdentifier.
    /// Used by the client to dedupe the deck tube against the active preview when the
    /// host's shuffledDeck transiently still holds a reference to the same instance.
    /// May be null/empty when host can't resolve a GUID; clients fall back to name-only.
    /// </summary>
    public string CurrentOrbGuid { get; set; }

    public int CurrentOrbLevel { get; set; }

    public int DeckSize { get; set; }
}

public class OrbEntry
{
    public string Guid { get; set; }

    public int DeckIndex { get; set; }

    public string Name { get; set; }

    public string LocName { get; set; }

    public int Level { get; set; }

    public int BaseDamage { get; set; }

    public int CritDamage { get; set; }
}
