namespace PeglinMods.Multiplayer.Events.Handlers.Health;

using PeglinMods.Multiplayer.Events.Network.Health;

public sealed class ArmourHitServerHandler : IServerHandler<ArmourHitEvent>
{
    public ArmourHitEvent Handle(ArmourHitEvent networkEvent) => networkEvent;
}
