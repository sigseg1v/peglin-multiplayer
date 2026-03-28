namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class RoundIncrementedServerHandler : IServerHandler<RoundIncrementedEvent>
{
    public RoundIncrementedEvent Handle(RoundIncrementedEvent networkEvent) => networkEvent;
}
