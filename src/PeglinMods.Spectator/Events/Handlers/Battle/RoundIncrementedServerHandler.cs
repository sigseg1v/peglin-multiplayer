namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class RoundIncrementedServerHandler : IServerHandler<RoundIncrementedEvent>
{
    public RoundIncrementedEvent Handle(RoundIncrementedEvent networkEvent) => networkEvent;
}
