namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class BattleStartedServerHandler : IServerHandler<BattleStartedEvent>
{
    public BattleStartedEvent Handle(BattleStartedEvent networkEvent) => networkEvent;
}
