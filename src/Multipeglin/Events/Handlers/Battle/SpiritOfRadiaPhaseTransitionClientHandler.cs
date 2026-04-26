using System;
using global::Battle.Enemies;
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class SpiritOfRadiaPhaseTransitionClientHandler : IClientHandler<SpiritOfRadiaPhaseTransitionEvent>
{
    public void Handle(SpiritOfRadiaPhaseTransitionEvent networkEvent)
    {
        try
        {
            switch (networkEvent.Step)
            {
                case 1:
                    MultiplayerPlugin.Logger?.LogInfo("[SpiritOfRadia] Client: firing PreTransitionStarted");
                    SpiritOfRadiaBoss.PreTransitionStarted?.Invoke();
                    break;
                case 2:
                    MultiplayerPlugin.Logger?.LogInfo("[SpiritOfRadia] Client: firing OnSpiritOfRadiaPhaseTransitionStarted");
                    SpiritOfRadiaBoss.OnSpiritOfRadiaPhaseTransitionStarted?.Invoke();
                    break;
                default:
                    MultiplayerPlugin.Logger?.LogWarning($"[SpiritOfRadia] Unknown step {networkEvent.Step}");
                    break;
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SpiritOfRadia] Client handler failed: {e.Message}");
        }
    }
}
