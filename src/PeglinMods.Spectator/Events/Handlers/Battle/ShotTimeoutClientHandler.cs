namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

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
            SpectatorPlugin.Logger.LogWarning($"ShotTimeout handler failed: {e.Message}");
        }
    }
}
