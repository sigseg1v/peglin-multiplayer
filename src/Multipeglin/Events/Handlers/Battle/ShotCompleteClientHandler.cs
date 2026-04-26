using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class ShotCompleteClientHandler : IClientHandler<ShotCompleteEvent>
{
    public void Handle(ShotCompleteEvent networkEvent)
    {
        try
        {
            BattleController.OnShotComplete?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"ShotComplete handler failed: {e.Message}");
        }
    }
}
