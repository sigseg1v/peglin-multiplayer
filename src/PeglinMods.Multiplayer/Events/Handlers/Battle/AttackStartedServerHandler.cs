namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class AttackStartedServerHandler : IServerHandler<AttackStartedEvent>
{
    public AttackStartedEvent Handle(AttackStartedEvent networkEvent) => networkEvent;
}
