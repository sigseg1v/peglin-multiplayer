using PeglinMods.Multiplayer.Events.Network.Scenarios;

namespace PeglinMods.Multiplayer.Events.Handlers.Scenarios;

/// <summary>
/// Server handler for TextScenarioCompleteEvent (client → host).
/// Suppresses rebroadcast — the host processes results directly.
/// </summary>
public sealed class TextScenarioCompleteServerHandler : IServerHandler<TextScenarioCompleteEvent>
{
    public TextScenarioCompleteEvent Handle(TextScenarioCompleteEvent networkEvent)
    {
        MultiplayerPlugin.Logger?.LogInfo("[TextScenarioCompleteServer] Received — suppressing rebroadcast");
        return null;
    }
}
