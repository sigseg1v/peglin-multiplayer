namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

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
            SpectatorPlugin.Logger.LogWarning($"ShotComplete handler failed: {e.Message}");
        }
    }
}
