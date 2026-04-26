
using Multipeglin.Events.Network.Currency;

namespace Multipeglin.Events.Handlers.Currency;
public sealed class GoldChangedServerHandler : IServerHandler<GoldChangedEvent>
{
    public GoldChangedEvent Handle(GoldChangedEvent networkEvent) => networkEvent;
}
