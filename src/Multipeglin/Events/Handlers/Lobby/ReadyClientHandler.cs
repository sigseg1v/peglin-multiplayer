namespace Multipeglin.Events.Handlers.Lobby;

using Multipeglin.Events.Network.Lobby;
using Multipeglin.Multiplayer;

public sealed class ReadyClientHandler : IClientHandler<ReadyEvent>
{
    public void Handle(ReadyEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null)
            return;

        if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting)
            return;
        if (!services.TryResolve<PlayerRegistry>(out var registry))
            return;
        if (!services.TryResolve<Events.IGameEventRegistry>(out var eventRegistry))
            return;

        var senderPeerId = (eventRegistry as Events.GameEventRegistry)?.CurrentSenderPeerId ?? -1;
        var slot = registry.GetSlotByPeerId(senderPeerId);
        if (slot == null)
            return;

        slot.IsReady = networkEvent.IsReady;
        MultiplayerPlugin.Logger?.LogInfo($"[Ready] {slot.PlayerName} is {(networkEvent.IsReady ? "READY" : "NOT READY")}");

        LobbyHelper.BroadcastLobbyState(registry, eventRegistry);
    }
}
