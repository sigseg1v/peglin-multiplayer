namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class BattleEndedServerHandler : IServerHandler<BattleEndedEvent>
{
    public BattleEndedEvent Handle(BattleEndedEvent networkEvent) => networkEvent;
}
