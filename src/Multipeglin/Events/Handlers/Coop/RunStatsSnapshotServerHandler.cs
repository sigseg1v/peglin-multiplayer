using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>Pure rebroadcast — host dispatches, every client receives.</summary>
public sealed class RunStatsSnapshotServerHandler : IServerHandler<RunStatsSnapshotEvent>
{
    public RunStatsSnapshotEvent Handle(RunStatsSnapshotEvent networkEvent) => networkEvent;
}
