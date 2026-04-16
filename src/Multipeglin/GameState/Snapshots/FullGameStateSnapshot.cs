using System.Collections.Generic;

namespace Multipeglin.GameState.Snapshots;

/// <summary>
/// Complete game state at a point in time. Sent on connect and at major transitions.
/// Individual slices can also be sent separately for targeted updates.
/// </summary>
public class FullGameStateSnapshot
{
    public long TimestampMs { get; set; }
    public MapStateSnapshot Map { get; set; }
    public EnemyStateSnapshot Enemies { get; set; }
    public PegboardStateSnapshot Pegboard { get; set; }

    // Active player state (loaded into game singletons)
    public PlayerStateSnapshot Player { get; set; }
    public DeckStateSnapshot Deck { get; set; }
    public RelicStateSnapshot Relics { get; set; }

    // Co-op multi-player state
    public int ActivePlayerSlot { get; set; }
    public int TotalPlayerCount { get; set; }
    public Dictionary<int, DeckStateSnapshot> AllDecks { get; set; }
    public Dictionary<int, RelicStateSnapshot> AllRelics { get; set; }
    public List<CoopPlayerSummary> PlayerSummaries { get; set; }

    // TextScenario dialogue state (for spectating event scenes)
    public TextScenarioStateSnapshot TextScenario { get; set; }
}

/// <summary>
/// Lightweight per-player summary sent in every heartbeat.
/// Clients use this for the battle HUD (name, HP, class).
/// </summary>
public class CoopPlayerSummary
{
    public int SlotIndex { get; set; }
    public string PlayerName { get; set; }
    public int ChosenClass { get; set; }
    public float CurrentHealth { get; set; }
    public float MaxHealth { get; set; }
    public int Gold { get; set; }
    public bool HasShotThisRound { get; set; }
    public List<StatusEffectEntry> StatusEffects { get; set; } = new List<StatusEffectEntry>();
}
