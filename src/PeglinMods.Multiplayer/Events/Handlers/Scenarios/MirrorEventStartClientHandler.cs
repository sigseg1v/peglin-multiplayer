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
        try
        {
            var mode = MultiplayerPlugin.Services?.Resolve<IMultiplayerMode>();
            if (mode == null || !mode.IsSpectating) return;

            MultiplayerPlugin.Logger?.LogInfo(
                $"[MirrorEventStart] Received mirror event start — scenario='{e.ScenarioName}', showing interactive UI");

            MirrorEventUI.Show();
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[MirrorEventStart] Handler failed: {ex.Message}");
        }
    }
}
