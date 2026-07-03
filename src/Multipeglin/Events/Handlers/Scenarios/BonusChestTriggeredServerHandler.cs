using Multipeglin.Events.Network.Scenarios;

namespace Multipeglin.Events.Handlers.Scenarios;

/// <summary>
/// Server handler for BonusChestTriggeredEvent. Runs on the host both for its
/// own Dispatch (host detonated the last bomb) and for client reports. First
/// trigger wins: applies the transition on the host and rebroadcasts so every
/// client transitions to the bonus TREASURE room. Late/duplicate triggers are
/// swallowed (return null = no rebroadcast).
/// </summary>
public sealed class BonusChestTriggeredServerHandler : IServerHandler<BonusChestTriggeredEvent>
{
    public BonusChestTriggeredEvent Handle(BonusChestTriggeredEvent networkEvent)
    {
        if (BonusChestSync.TransitionStarted)
        {
            MultiplayerPlugin.Logger?.LogInfo("[BonusChest] Duplicate trigger ignored (transition already started)");
            return null;
        }

        BonusChestSync.ApplyLocally(networkEvent.Source, isHost: true);
        return networkEvent;
    }
}
