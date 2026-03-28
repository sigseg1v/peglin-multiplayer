namespace PeglinMods.Multiplayer.GameState.Snapshots;

/// <summary>
/// Complete game state at a point in time. Sent on connect and at major transitions.
/// Individual slices can also be sent separately for targeted updates.
/// </summary>
public class FullGameStateSnapshot
{
    public long TimestampMs { get; set; }
    public MapStateSnapshot Map { get; set; }
    public PlayerStateSnapshot Player { get; set; }
    public DeckStateSnapshot Deck { get; set; }
    public RelicStateSnapshot Relics { get; set; }
    public EnemyStateSnapshot Enemies { get; set; }
    public PegboardStateSnapshot Pegboard { get; set; }
}
