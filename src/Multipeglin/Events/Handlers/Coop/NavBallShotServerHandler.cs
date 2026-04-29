using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Host-side relay for NavBallShotEvent. Stamps the sender's slot (clients
/// don't trust their own slot index) and returns the event so it's rebroadcast
/// to every connected client. Also fires locally on the host so the host sees
/// the client's ball — the matching ClientHandler runs on every receiver.
/// </summary>
public sealed class NavBallShotServerHandler : IServerHandler<NavBallShotEvent>
{
    public NavBallShotEvent Handle(NavBallShotEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return networkEvent;
        }

        if (!services.TryResolve<PlayerRegistry>(out var registry)
            || !services.TryResolve<GameEventRegistry>(out var ger))
        {
            return networkEvent;
        }

        var senderPeerId = ger.CurrentSenderPeerId;
        var slot = registry.GetSlotByPeerId(senderPeerId);
        if (slot != null)
        {
            networkEvent.Slot = slot.SlotIndex;
        }

        return networkEvent;
    }
}
