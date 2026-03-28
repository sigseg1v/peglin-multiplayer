namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class AwaitingShotServerHandler : IServerHandler<AwaitingShotEvent>
{
    public AwaitingShotEvent Handle(AwaitingShotEvent networkEvent) => networkEvent;
}
