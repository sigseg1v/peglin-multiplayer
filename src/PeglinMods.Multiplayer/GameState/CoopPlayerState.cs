using System.Collections.Generic;

namespace PeglinMods.Multiplayer.GameState;

/// <summary>
/// Stores the complete game state for one player in the co-op session.
/// On the host, each player's state is saved/restored from game singletons
/// when swapping active players.
/// </summary>
public class CoopPlayerState
{
    public int SlotIndex { get; set; }
    public string PlayerName { get; set; }
    public int ChosenClass { get; set; }

    // Health
    public float CurrentHealth { get; set; }
    public float MaxHealth { get; set; }

    // Gold
    public int Gold { get; set; }

    // Deck: stored as serialized orb data (prefab names + levels)
    public List<SerializedOrb> CompleteDeck { get; set; } = new List<SerializedOrb>();
    public List<SerializedOrb> BattleDeck { get; set; } = new List<SerializedOrb>();
    public List<string> ShuffledOrder { get; set; } = new List<string>();
    public string CurrentOrb { get; set; }
    public int CurrentOrbLevel { get; set; }

    // Relics
    public List<SerializedRelic> OwnedRelics { get; set; } = new List<SerializedRelic>();
    public Dictionary<int, int> RelicCountdowns { get; set; } = new Dictionary<int, int>();

    // Status effects
    public List<SerializedStatusEffect> StatusEffects { get; set; } = new List<SerializedStatusEffect>();

    // Per-round tracking
    public bool HasShotThisRound { get; set; }

    // Initialized flag (deck/relics loaded from ClassLoadoutData)
    public bool IsInitialized { get; set; }
}

public class SerializedOrb
{
    public string PrefabName { get; set; }
    public int Level { get; set; }
}

public class SerializedRelic
{
    public int Effect { get; set; }
    public string LocKey { get; set; }
    public int Rarity { get; set; }
}

public class SerializedStatusEffect
{
    public int EffectType { get; set; }
    public int Intensity { get; set; }
}
