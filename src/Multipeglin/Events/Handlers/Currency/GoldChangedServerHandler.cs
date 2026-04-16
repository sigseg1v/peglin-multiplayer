namespace Multipeglin.Events.Handlers.Currency;

using Multipeglin.Events.Network.Currency;

public sealed class GoldChangedServerHandler : IServerHandler<GoldChangedEvent>
{
    public GoldChangedEvent Handle(GoldChangedEvent networkEvent) => networkEvent;
}
