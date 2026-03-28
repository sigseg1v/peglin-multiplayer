namespace PeglinMods.Multiplayer.Events.Handlers.Currency;

using PeglinMods.Multiplayer.Events.Network.Currency;

public sealed class GoldChangedServerHandler : IServerHandler<GoldChangedEvent>
{
    public GoldChangedEvent Handle(GoldChangedEvent networkEvent) => networkEvent;
}
