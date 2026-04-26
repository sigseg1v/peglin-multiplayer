using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class RoundIncrementedServerHandler : IServerHandler<RoundIncrementedEvent>
{
    public RoundIncrementedEvent Handle(RoundIncrementedEvent networkEvent) => networkEvent;
}
