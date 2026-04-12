using PeglinMods.Multiplayer.Events.Network.Scenarios;

namespace PeglinMods.Multiplayer.Events.Handlers.Scenarios;

/// <summary>
/// Server handler for TreasureCompleteEvent (client -> host).
/// Suppresses rebroadcast — the host processes results directly.
/// </summary>
public sealed class TreasureCompleteServerHandler : IServerHandler<TreasureCompleteEvent>
{
    public TreasureCompleteEvent Handle(TreasureCompleteEvent networkEvent)
    {
        MultiplayerPlugin.Logger?.LogInfo("[TreasureCompleteServer] Received — suppressing rebroadcast");
        return null;
    }
}
