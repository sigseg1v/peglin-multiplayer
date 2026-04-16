namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class RoundIncrementedServerHandler : IServerHandler<RoundIncrementedEvent>
{
    public RoundIncrementedEvent Handle(RoundIncrementedEvent networkEvent) => networkEvent;
}
