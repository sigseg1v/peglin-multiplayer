namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class ShotTimeoutServerHandler : IServerHandler<ShotTimeoutEvent>
{
    public ShotTimeoutEvent Handle(ShotTimeoutEvent networkEvent) => networkEvent;
}
