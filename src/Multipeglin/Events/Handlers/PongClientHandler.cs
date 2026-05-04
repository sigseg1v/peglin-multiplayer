using Multipeglin.Events.Network;
using Multipeglin.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>
/// Client-side: hand the pong to the AppLevelRttProvider so it can match
/// the token against an outstanding ping and update the RTT estimate.
/// </summary>
public sealed class PongClientHandler : IClientHandler<PongEvent>
{
    public void Handle(PongEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null
            || !services.TryResolve<AppLevelRttProvider>(out var rtt))
        {
            return;
        }

        rtt.OnPongReceived(networkEvent.Token);
    }
}
