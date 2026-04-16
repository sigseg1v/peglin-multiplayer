namespace Multipeglin.GameState;

/// <summary>
/// Orchestrates capturing all game state and sending it to connected clients.
/// Called by event subscriptions at key game moments.
/// </summary>
public interface IGameStateSyncService
{
    void SyncAll(string trigger = null);
    void SyncMap();
    void SyncPegboard();
    void SyncEnemies();
    void SyncPlayer();
    void SyncDeck();
    void SyncRelics();
}
