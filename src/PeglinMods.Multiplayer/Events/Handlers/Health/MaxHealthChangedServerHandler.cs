namespace PeglinMods.Multiplayer.Events.Handlers.Health;

using PeglinMods.Multiplayer.Events.Network.Health;

public sealed class MaxHealthChangedServerHandler : IServerHandler<MaxHealthChangedEvent>
{
    public MaxHealthChangedEvent Handle(MaxHealthChangedEvent networkEvent) => networkEvent;
}
