namespace PeglinMods.Spectator.Events.Handlers.Ball;

using PeglinMods.Spectator.Events.Network.Ball;

public sealed class ShotFiredServerHandler : IServerHandler<ShotFiredEvent>
{
    public ShotFiredEvent Handle(ShotFiredEvent networkEvent) => networkEvent;
}
