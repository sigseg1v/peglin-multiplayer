using PeglinMods.Multiplayer.Events.Network.Scenarios;

namespace PeglinMods.Multiplayer.Events.Handlers.Scenarios;

/// <summary>
/// Server handler for PegMinigameCompleteEvent (client -> host).
/// Suppresses rebroadcast — the host processes results directly.
/// </summary>
public sealed class PegMinigameCompleteServerHandler : IServerHandler<PegMinigameCompleteEvent>
{
    public PegMinigameCompleteEvent Handle(PegMinigameCompleteEvent networkEvent)
    {
        MultiplayerPlugin.Logger?.LogInfo("[PegMinigameCompleteServer] Received — suppressing rebroadcast");
        return null;
    }
}
