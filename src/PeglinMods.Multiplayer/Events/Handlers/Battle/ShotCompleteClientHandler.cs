namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

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
