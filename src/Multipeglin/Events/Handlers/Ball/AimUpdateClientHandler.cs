using System;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Ball;
using Multipeglin.GameState;

namespace Multipeglin.Events.Handlers.Ball;

public sealed class AimUpdateClientHandler : IClientHandler<AimUpdateEvent>
{
    public void Handle(AimUpdateEvent e)
    {
        try
        {
            // Skip rendering when it's the local player's own turn — they already
            // have native trajectory rendering via PredictionManager/TrajectorySimulation.
            // This event is a rebroadcast from the host of our own aim data.
            if (TurnChangeClientHandler.IsMyTurn)
                return;

            ClientAimRenderer.Instance?.UpdateAim(e.AimX, e.AimY, e.SpawnX, e.SpawnY);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"AimUpdate handler failed: {ex.Message}");
        }
    }
}
