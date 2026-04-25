namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class SpiritOfRadiaPhaseTransitionServerHandler : IServerHandler<SpiritOfRadiaPhaseTransitionEvent>
{
    public SpiritOfRadiaPhaseTransitionEvent Handle(SpiritOfRadiaPhaseTransitionEvent networkEvent) => networkEvent;
}
