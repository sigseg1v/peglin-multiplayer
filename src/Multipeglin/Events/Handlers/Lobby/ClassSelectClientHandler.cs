using Multipeglin.Events.Network.Lobby;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Lobby;

/// <summary>
/// On host: a client sent their class selection. Update the registry and broadcast lobby state.
/// On client: this event is not rebroadcast (server handler returns null), so clients
/// receive lobby state via LobbyStateEvent instead.
/// </summary>
public sealed class ClassSelectClientHandler : IClientHandler<ClassSelectEvent>
{
    public void Handle(ClassSelectEvent networkEvent)
    {
        // This fires on the HOST when receiving from a client.
        // The host's GameEventRegistry.HandleIncoming is called for client→host events.
        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return;
        }

        if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting)
        {
            return;
        }

        if (!services.TryResolve<PlayerRegistry>(out var registry))
        {
            return;
        }

        if (!services.TryResolve<Events.IGameEventRegistry>(out var eventRegistry))
        {
            return;
        }

        var senderPeerId = (eventRegistry as Events.GameEventRegistry)?.CurrentSenderPeerId ?? -1;
        var slot = registry.GetSlotByPeerId(senderPeerId);
        if (slot == null)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClassSelect] No slot for peer {senderPeerId}");
            return;
        }

        slot.ChosenClass = networkEvent.ChosenClass;
        MultiplayerPlugin.Logger?.LogInfo($"[ClassSelect] {slot.PlayerName} chose class {networkEvent.ChosenClass}");

        // Broadcast updated lobby state to all
        LobbyHelper.BroadcastLobbyState(registry, eventRegistry);
    }
}
