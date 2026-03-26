namespace PeglinMods.Spectator.Events.Handlers.Currency;

using PeglinMods.Spectator.Events.Network.Currency;

public sealed class GoldChangedServerHandler : IServerHandler<GoldChangedEvent>
{
    public GoldChangedEvent Handle(GoldChangedEvent networkEvent) => networkEvent;
}
