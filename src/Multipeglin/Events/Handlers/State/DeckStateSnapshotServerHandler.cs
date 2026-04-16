using Multipeglin.GameState.Snapshots;

namespace Multipeglin.Events.Handlers.State;

public sealed class DeckStateSnapshotServerHandler : IServerHandler<DeckStateSnapshot>
{
    public DeckStateSnapshot Handle(DeckStateSnapshot e) => e;
}
