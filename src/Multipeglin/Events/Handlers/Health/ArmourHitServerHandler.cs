namespace Multipeglin.Events.Handlers.Health;

using Multipeglin.Events.Network.Health;

public sealed class ArmourHitServerHandler : IServerHandler<ArmourHitEvent>
{
    public ArmourHitEvent Handle(ArmourHitEvent networkEvent) => networkEvent;
}
