using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class ReloadStartedServerHandler : IServerHandler<ReloadStartedEvent>
{
    public ReloadStartedEvent Handle(ReloadStartedEvent networkEvent) => networkEvent;
}
