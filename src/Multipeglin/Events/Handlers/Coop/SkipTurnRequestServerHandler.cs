using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

public sealed class SkipTurnRequestServerHandler : IServerHandler<SkipTurnRequestEvent>
{
    public SkipTurnRequestEvent Handle(SkipTurnRequestEvent networkEvent) => null;
}
