using Multipeglin.Events.Network.Health;

namespace Multipeglin.Events.Handlers.Health;

public sealed class ArmourHitServerHandler : IServerHandler<ArmourHitEvent>
{
    public ArmourHitEvent Handle(ArmourHitEvent networkEvent) => networkEvent;
}
