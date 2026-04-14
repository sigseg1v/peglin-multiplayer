using System;
using PeglinMods.Multiplayer.Events.Network.Scenarios;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.UI;

namespace PeglinMods.Multiplayer.Events.Handlers.Scenarios;

/// <summary>
/// Client handler for MirrorEventStartEvent: shows the interactive Mirror UI
/// so the client can choose to remove an orb or remove all orbs (get Orboros).
/// </summary>
public sealed class MirrorEventStartClientHandler : IClientHandler<MirrorEventStartEvent>
{
    public void Handle(MirrorEventStartEvent e)
    {
        // No-op: clients now handle TextScenario dialogue natively via AllowTextScenarioLogic.
        // The custom MirrorEventUI is no longer shown.
        MultiplayerPlugin.Logger?.LogInfo(
            $"[MirrorEventStart] Received mirror event start — scenario='{e.ScenarioName}' (no-op, native dialogue handles this)");
    }
}
