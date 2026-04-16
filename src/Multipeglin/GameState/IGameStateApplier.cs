namespace Multipeglin.GameState;

/// <summary>
/// Applies a received state snapshot to the live game.
/// Each applier handles one domain (map, enemies, pegs, etc.).
/// Inverse of IGameStateProvider.
/// </summary>
public interface IGameStateApplier<TState> where TState : class
{
    void Apply(TState snapshot);
}
