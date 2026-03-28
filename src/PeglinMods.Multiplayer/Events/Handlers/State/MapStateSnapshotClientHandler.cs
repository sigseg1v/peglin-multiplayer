using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class MapStateSnapshotClientHandler : IClientHandler<MapStateSnapshot>
{
    public void Handle(MapStateSnapshot e)
    {
        MultiplayerPlugin.Logger?.LogInfo($"[StateSync] Map: scene={e.ActiveScene}, floor={e.TotalFloorCount}, class={e.ChosenClassName}, seed={e.CurrentSeed}, boss={e.HasReachedBoss}");
    }
}
