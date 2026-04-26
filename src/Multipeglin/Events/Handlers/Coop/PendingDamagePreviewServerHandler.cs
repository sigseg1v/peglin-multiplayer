using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Rebroadcast PendingDamagePreviewEvent to all clients so they can
/// render the same damage overlay as the host.
/// </summary>
public sealed class PendingDamagePreviewServerHandler : IServerHandler<PendingDamagePreviewEvent>
{
    public PendingDamagePreviewEvent Handle(PendingDamagePreviewEvent networkEvent)
    {
        return networkEvent; // Rebroadcast to clients
    }
}
