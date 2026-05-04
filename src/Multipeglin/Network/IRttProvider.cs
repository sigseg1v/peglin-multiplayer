namespace Multipeglin.Network;

/// <summary>
/// Transport-agnostic round-trip-time source. The Steam P2P API we use
/// (ISteamNetworking.SendP2PPacket) doesn't surface ping the way LiteNetLib
/// does, so for full coverage we measure at the application layer via a
/// ping/pong heartbeat — that path works for both LiteNet and Steam.
/// </summary>
public interface IRttProvider
{
    /// <summary>
    /// Most recent observed round-trip in milliseconds, or 0 if no measurement
    /// is available yet (just connected, or no peers).
    /// </summary>
    int CurrentRttMs { get; }
}
