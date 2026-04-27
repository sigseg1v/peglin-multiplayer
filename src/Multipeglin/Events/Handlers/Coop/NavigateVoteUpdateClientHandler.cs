using System.Collections.Generic;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client handler for NavigateVoteUpdateEvent: refreshes the local tally so the
/// slot-tally HUD can repaint. Host already updates state directly.
/// </summary>
public sealed class NavigateVoteUpdateClientHandler : IClientHandler<NavigateVoteUpdateEvent>
{
    public void Handle(NavigateVoteUpdateEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting)
        {
            return; // host updates its own state directly via the resolver
        }

        if (networkEvent.VoteCounts == null)
        {
            return;
        }

        if (!CoopNavigateState.PhaseActive)
        {
            // Update can arrive before NavigatePhaseStartEvent if reordered; bootstrap.
            CoopNavigateState.PhaseActive = true;
            CoopNavigateState.ChildNodeCount = networkEvent.VoteCounts.Count;
        }

        CoopNavigateState.VoteCounts = new List<int>(networkEvent.VoteCounts);
    }
}
