using Multipeglin.Events.Network.Scenarios;

namespace Multipeglin.Events.Handlers.Scenarios;

/// <summary>
/// Server handler for ShopPurchaseEvent (client -> host).
/// Host-only: the actual state mutation happens in the ClientHandler
/// (which runs on the host because the event is incoming from a client
/// and the host's NetworkHost dispatches via server+client handlers).
/// Suppresses rebroadcast — other clients don't need per-purchase events.
/// </summary>
public sealed class ShopPurchaseServerHandler : IServerHandler<ShopPurchaseEvent>
{
    public ShopPurchaseEvent Handle(ShopPurchaseEvent networkEvent)
    {
        return null; // don't rebroadcast
    }
}
