namespace Multipeglin.Events.Handlers.Battle;

using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;

public sealed class ShotTimeoutClientHandler : IClientHandler<ShotTimeoutEvent>
{
    public void Handle(ShotTimeoutEvent networkEvent)
    {
        try
        {
            BattleController.OnShotTimeout?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"ShotTimeout handler failed: {e.Message}");
        }
    }
}
