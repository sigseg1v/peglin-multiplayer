namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class ReloadStartedServerHandler : IServerHandler<ReloadStartedEvent>
{
    public ReloadStartedEvent Handle(ReloadStartedEvent networkEvent) => networkEvent;
}
