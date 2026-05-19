// commented out for performance: see class body below.
// using Multipeglin.Events.Network.Relic;

namespace Multipeglin.Events.Handlers.Relic;

// commented out for performance: per-peg-hit relics (ALL_ORBS_BUFF,
// ALL_ORBS_DEBUFF, etc.) drove this handler thousands of times per shot,
// rebroadcasting to clients where it did nothing but log. no consumer
// elsewhere needs RelicUsedEvent today.
/*
public sealed class RelicUsedServerHandler : IServerHandler<RelicUsedEvent>
{
    public RelicUsedEvent Handle(RelicUsedEvent networkEvent) => networkEvent;
}
*/
