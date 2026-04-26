
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;
public sealed class ShotTimeoutServerHandler : IServerHandler<ShotTimeoutEvent>
{
    public ShotTimeoutEvent Handle(ShotTimeoutEvent networkEvent) => networkEvent;
}
