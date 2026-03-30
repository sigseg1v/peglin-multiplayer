using PeglinMods.Multiplayer.Events.Network.Ball;

namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

public sealed class AimUpdateServerHandler : IServerHandler<AimUpdateEvent>
{
    public AimUpdateEvent Handle(AimUpdateEvent e) => e;
}
