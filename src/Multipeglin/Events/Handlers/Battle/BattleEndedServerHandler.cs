namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class BattleEndedServerHandler : IServerHandler<BattleEndedEvent>
{
    public BattleEndedEvent Handle(BattleEndedEvent networkEvent) => networkEvent;
}
