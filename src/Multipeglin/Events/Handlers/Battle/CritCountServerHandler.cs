using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class CritCountServerHandler : IServerHandler<CritCountEvent>
{
    public CritCountEvent Handle(CritCountEvent networkEvent) => networkEvent;
}
