namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class VictoryServerHandler : IServerHandler<VictoryEvent>
{
    public VictoryEvent Handle(VictoryEvent networkEvent) => networkEvent;
}
