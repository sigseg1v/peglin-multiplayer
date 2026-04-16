namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class BattleStartedServerHandler : IServerHandler<BattleStartedEvent>
{
    public BattleStartedEvent Handle(BattleStartedEvent networkEvent) => networkEvent;
}
