namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class ReloadStartedServerHandler : IServerHandler<ReloadStartedEvent>
{
    public ReloadStartedEvent Handle(ReloadStartedEvent networkEvent) => networkEvent;
}
