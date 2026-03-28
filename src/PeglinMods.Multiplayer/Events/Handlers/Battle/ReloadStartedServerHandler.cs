namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class ReloadStartedServerHandler : IServerHandler<ReloadStartedEvent>
{
    public ReloadStartedEvent Handle(ReloadStartedEvent networkEvent) => networkEvent;
}
