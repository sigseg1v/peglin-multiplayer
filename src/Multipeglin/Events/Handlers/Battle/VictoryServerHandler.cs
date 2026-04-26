using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class VictoryServerHandler : IServerHandler<VictoryEvent>
{
    public VictoryEvent Handle(VictoryEvent networkEvent) => networkEvent;
}
