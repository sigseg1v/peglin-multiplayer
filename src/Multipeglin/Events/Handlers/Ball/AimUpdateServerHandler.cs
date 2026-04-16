using Multipeglin.Events.Network.Ball;
using Multipeglin.GameState;

namespace Multipeglin.Events.Handlers.Ball;

public sealed class AimUpdateServerHandler : IServerHandler<AimUpdateEvent>
{
    public AimUpdateEvent Handle(AimUpdateEvent e)
    {
        // When a client sends an aim update, render it on the host so the host
        // can see where the client is aiming during their turn.
        // Skip rendering when it's the host's own turn (slot 0) — the host has
        // native trajectory rendering and this event came from BallPositionSync.Dispatch().
        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<TurnManager>(out var tm) == true && tm.CurrentPlayerSlot > 0)
        {
            ClientAimRenderer.Instance?.UpdateAim(e.AimX, e.AimY, e.SpawnX, e.SpawnY);
        }
        return e;
    }
}
