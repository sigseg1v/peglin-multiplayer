using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class DeckStateSnapshotServerHandler : IServerHandler<DeckStateSnapshot>
{
    public DeckStateSnapshot Handle(DeckStateSnapshot e) => e;
}
