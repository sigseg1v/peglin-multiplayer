namespace PeglinMods.Multiplayer.GameState;

/// <summary>
/// Reads a specific slice of game state from the running game.
/// Each provider handles one domain (map, enemies, pegs, etc.).
/// </summary>
public interface IGameStateProvider<TState> where TState : class
{
    /// <summary>
    /// Capture current state from the live game. Returns null if not available
    /// (e.g. not in a battle scene for enemy state).
    /// </summary>
    TState Capture();
}
