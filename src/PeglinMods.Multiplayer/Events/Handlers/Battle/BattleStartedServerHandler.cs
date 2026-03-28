namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class BattleStartedServerHandler : IServerHandler<BattleStartedEvent>
{
    public BattleStartedEvent Handle(BattleStartedEvent networkEvent) => networkEvent;
}
