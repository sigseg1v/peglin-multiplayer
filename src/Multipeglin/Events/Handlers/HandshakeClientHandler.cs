using Multipeglin.Events.Handlers.Lobby;
using Multipeglin.Events.Network;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers;

public sealed class HandshakeClientHandler : IClientHandler<HandshakeEvent>
{
    public void Handle(HandshakeEvent networkEvent)
    {
        var log = MultiplayerPlugin.Logger;

        log.LogInfo($"Received handshake from {(networkEvent.IsHost ? "HOST" : "CLIENT")} '{networkEvent.PlayerName}':");
        log.LogInfo($"  Mod version: {networkEvent.ModVersion}");
        log.LogInfo($"  Compiled for Peglin: {networkEvent.CompiledGameVersion}");
        log.LogInfo($"  Running Peglin: {networkEvent.RuntimeGameVersion}");
        log.LogInfo($"  Registered handlers: {networkEvent.RegisteredHandlerCount}");

        var localMod = MultiplayerPluginInfo.VERSION;
        var localGame = UnityEngine.Application.version ?? "unknown";

        if (networkEvent.ModVersion != localMod)
        {
            log.LogWarning($"MOD VERSION MISMATCH: remote={networkEvent.ModVersion} local={localMod}");
        }

        if (networkEvent.RuntimeGameVersion != localGame)
        {
            log.LogWarning($"GAME VERSION MISMATCH: remote={networkEvent.RuntimeGameVersion} local={localGame}");
        }

        // Store remote info for UI display
        RemotePeerInfo.PlayerName = networkEvent.PlayerName ?? "Unknown";
        RemotePeerInfo.ModVersion = networkEvent.ModVersion;
        RemotePeerInfo.GameVersion = networkEvent.RuntimeGameVersion;
        RemotePeerInfo.IsHost = networkEvent.IsHost;
        RemotePeerInfo.HandlerCount = networkEvent.RegisteredHandlerCount;
        RemotePeerInfo.Received = true;

        // On host: register the client in the PlayerRegistry and broadcast lobby state
        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting && !networkEvent.IsHost)
        {
            if (services.TryResolve<PlayerRegistry>(out var registry))
            {
                var senderPeerId = (services.TryResolve<IGameEventRegistry>(out var er)
                    ? (er as GameEventRegistry)?.CurrentSenderPeerId : null) ?? -1;
                var slot = registry.GetSlotByPeerId(senderPeerId);
                if (slot == null)
                {
                    var name = networkEvent.PlayerName ?? "Unknown";

                    // Continue mode: only roster members may join, and they go to
                    // the slot they had when the save was written.
                    if (Continue.ContinueSession.IsActive)
                    {
                        var expectedSlot = Continue.ContinueSession.GetSlotForPlayer(name);
                        if (expectedSlot < 0)
                        {
                            log.LogWarning($"[Lobby] Continue: rejecting '{name}' — not in saved roster");
                            return;
                        }

                        var newSlot = registry.RegisterClientWithSlot(senderPeerId, name, expectedSlot, networkEvent.RuntimeGameVersion ?? "unknown", networkEvent.ModVersion ?? "unknown");
                        log.LogInfo($"[Lobby] Continue: registered '{name}' into saved slot {newSlot.SlotIndex} (peerId={senderPeerId})");
                    }
                    else
                    {
                        var newSlot = registry.RegisterClient(senderPeerId, name, networkEvent.RuntimeGameVersion ?? "unknown", networkEvent.ModVersion ?? "unknown");
                        log.LogInfo($"[Lobby] Registered client '{name}' as slot {newSlot.SlotIndex} (peerId={senderPeerId})");
                    }
                }

                // Broadcast updated lobby state
                if (services.TryResolve<IGameEventRegistry>(out var eventRegistry))
                {
                    LobbyHelper.BroadcastLobbyState(registry, eventRegistry);
                }
            }
        }
    }
}

/// <summary>
/// Static holder for the remote peer's version info, readable by UI.
/// </summary>
public static class RemotePeerInfo
{
    public static bool Received;
    public static string PlayerName = string.Empty;
    public static string ModVersion = string.Empty;
    public static string GameVersion = string.Empty;
    public static bool IsHost;
    public static int HandlerCount;

    public static void Reset()
    {
        Received = false;
        PlayerName = string.Empty;
        ModVersion = string.Empty;
        GameVersion = string.Empty;
        IsHost = false;
        HandlerCount = 0;
    }
}
