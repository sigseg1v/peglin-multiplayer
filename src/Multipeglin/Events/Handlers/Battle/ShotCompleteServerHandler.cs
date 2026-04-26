using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class ShotCompleteServerHandler : IServerHandler<ShotCompleteEvent>
{
    public ShotCompleteEvent Handle(ShotCompleteEvent networkEvent) => networkEvent;
}
