namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class ShotTimeoutServerHandler : IServerHandler<ShotTimeoutEvent>
{
    public ShotTimeoutEvent Handle(ShotTimeoutEvent networkEvent) => networkEvent;
}
