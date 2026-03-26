namespace PeglinMods.Spectator.Events.Handlers.Health;

using PeglinMods.Spectator.Events.Network.Health;

public sealed class ArmourHitServerHandler : IServerHandler<ArmourHitEvent>
{
    public ArmourHitEvent Handle(ArmourHitEvent networkEvent) => networkEvent;
}
