namespace PeglinMods.Spectator.Events.Handlers.Health;

using PeglinMods.Spectator.Events.Network.Health;

public sealed class MaxHealthChangedServerHandler : IServerHandler<MaxHealthChangedEvent>
{
    public MaxHealthChangedEvent Handle(MaxHealthChangedEvent networkEvent) => networkEvent;
}
