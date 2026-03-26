namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class AwaitingShotServerHandler : IServerHandler<AwaitingShotEvent>
{
    public AwaitingShotEvent Handle(AwaitingShotEvent networkEvent) => networkEvent;
}
