using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class BattleStartedServerHandler : IServerHandler<BattleStartedEvent>
{
    public BattleStartedEvent Handle(BattleStartedEvent networkEvent) => networkEvent;
}
