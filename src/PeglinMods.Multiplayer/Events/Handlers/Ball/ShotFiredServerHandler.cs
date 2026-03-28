namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

using PeglinMods.Multiplayer.Events.Network.Ball;

public sealed class ShotFiredServerHandler : IServerHandler<ShotFiredEvent>
{
    public ShotFiredEvent Handle(ShotFiredEvent networkEvent) => networkEvent;
}
