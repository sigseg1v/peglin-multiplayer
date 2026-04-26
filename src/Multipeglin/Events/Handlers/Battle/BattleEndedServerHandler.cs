using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class BattleEndedServerHandler : IServerHandler<BattleEndedEvent>
{
    public BattleEndedEvent Handle(BattleEndedEvent networkEvent) => networkEvent;
}
