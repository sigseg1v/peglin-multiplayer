using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class SpiritOfRadiaPhaseTransitionServerHandler : IServerHandler<SpiritOfRadiaPhaseTransitionEvent>
{
    public SpiritOfRadiaPhaseTransitionEvent Handle(SpiritOfRadiaPhaseTransitionEvent networkEvent) => networkEvent;
}
