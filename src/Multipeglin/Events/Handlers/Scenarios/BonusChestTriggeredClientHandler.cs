using Multipeglin.Events.Network.Scenarios;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Handlers.Scenarios;

/// <summary>
/// Client handler for BonusChestTriggeredEvent (host -> clients): some player
/// detonated every navigation bomb, so this client transitions to the bonus
/// TREASURE room alongside everyone else.
/// </summary>
public sealed class BonusChestTriggeredClientHandler : IClientHandler<BonusChestTriggeredEvent>
{
    public void Handle(BonusChestTriggeredEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return;
        }

        // The host already applied during the server handler; Dispatch also
        // invokes the local client handler, which must not double-apply.
        if (services.TryResolve<IMultiplayerMode>(out var mode) && mode.IsHosting)
        {
            return;
        }

        BonusChestSync.ApplyLocally(networkEvent.Source, isHost: false);
    }
}
