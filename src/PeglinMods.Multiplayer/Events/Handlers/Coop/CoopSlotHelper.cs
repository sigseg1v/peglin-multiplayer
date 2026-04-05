using PeglinMods.Multiplayer.DI;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Network;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

/// <summary>
/// Utility to determine the local player's slot index.
/// The host is always slot 0. A client finds its slot as the first non-host entry
/// in the PlayerRegistry (the client only knows about itself and the host).
/// </summary>
public static class CoopSlotHelper
{
    /// <summary>
    /// Returns the local player's slot index, or -1 if it cannot be determined.
    /// </summary>
    public static int GetLocalSlotIndex(IServiceContainer services)
    {
        if (services == null) return -1;

        if (!services.TryResolve<PlayerRegistry>(out var registry)) return -1;
        if (!services.TryResolve<INetworkTransport>(out var transport)) return -1;

        if (transport.IsHost)
            return 0;

        // Client: check LocalSlot first (set by GameStartClientHandler)
        if (registry.LocalSlot != null)
            return registry.LocalSlot.SlotIndex;

        // Fallback: iterate registered slots
        foreach (var slot in registry.GetAllSlots())
        {
            if (!slot.IsHost)
                return slot.SlotIndex;
        }

        return -1;
    }
}
