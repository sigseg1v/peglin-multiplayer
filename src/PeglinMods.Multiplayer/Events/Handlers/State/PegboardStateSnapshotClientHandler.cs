using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class PegboardStateSnapshotClientHandler : IClientHandler<PegboardStateSnapshot>
{
    public void Handle(PegboardStateSnapshot e)
    {
        MultiplayerPlugin.Logger?.LogInfo($"[StateSync] Pegboard: {e.TotalPegCount} pegs ({e.CritPegCount} crit, {e.BombPegCount} bomb, {e.ResetPegCount} reset)");
    }
}
