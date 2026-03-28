namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class BattleEndedServerHandler : IServerHandler<BattleEndedEvent>
{
    public BattleEndedEvent Handle(BattleEndedEvent networkEvent) => networkEvent;
}
