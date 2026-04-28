using System.Collections.Generic;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client handler for NavigateResolvedEvent: marks the phase as resolved so the
/// local nav UI stops accepting votes and shows final colors. Scene transition
/// arrives via the existing scene-load sync (host's TriggerVictory / FadeAndLoad).
/// </summary>
public sealed class NavigateResolvedClientHandler : IClientHandler<NavigateResolvedEvent>
{
    public void Handle(NavigateResolvedEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting)
        {
            return; // host already updated state directly
        }

        MultiplayerPlugin.Logger?.LogInfo(
            $"[CoopNavigate] Client received navigate resolved: chosen={networkEvent.ChosenChildIndex}, tally=[{string.Join(",", networkEvent.VoteCounts ?? new List<int>())}]");

        // Clients no longer participate in the navigate vote — host runs nav-shot
        // solo. Clear any residual nav state so the next phase starts clean and
        // the PachinkoBall.Fire navigate-guard can't false-positive-block a
        // future shot if some legacy path had set PhaseActive.
        CoopNavigateState.Reset();
        Patches.MultiplayerClientPatches.AllowNavigateLogic = false;
    }
}
