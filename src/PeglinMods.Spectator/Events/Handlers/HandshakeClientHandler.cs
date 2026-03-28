using PeglinMods.Spectator.Events.Network;

namespace PeglinMods.Spectator.Events.Handlers;

public sealed class HandshakeClientHandler : IClientHandler<HandshakeEvent>
{
    public void Handle(HandshakeEvent networkEvent)
    {
        var log = SpectatorPlugin.Logger;

        log.LogInfo($"Received handshake from {(networkEvent.IsHost ? "HOST" : "CLIENT")} '{networkEvent.PlayerName}':");
        log.LogInfo($"  Mod version: {networkEvent.ModVersion}");
        log.LogInfo($"  Compiled for Peglin: {networkEvent.CompiledGameVersion}");
        log.LogInfo($"  Running Peglin: {networkEvent.RuntimeGameVersion}");
        log.LogInfo($"  Registered handlers: {networkEvent.RegisteredHandlerCount}");

        var localMod = SpectatorPluginInfo.VERSION;
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
    }
}

/// <summary>
/// Static holder for the remote peer's version info, readable by UI.
/// </summary>
public static class RemotePeerInfo
{
    public static bool Received;
    public static string PlayerName = "";
    public static string ModVersion = "";
    public static string GameVersion = "";
    public static bool IsHost;
    public static int HandlerCount;

    public static void Reset()
    {
        Received = false;
        PlayerName = "";
        ModVersion = "";
        GameVersion = "";
        IsHost = false;
        HandlerCount = 0;
    }
}
