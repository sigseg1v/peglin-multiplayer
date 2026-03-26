namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class AwaitingShotClientHandler : IClientHandler<AwaitingShotEvent>
{
    public void Handle(AwaitingShotEvent networkEvent)
    {
        try
        {
            BattleController.OnStartedAwaitingShot?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"AwaitingShot handler failed: {e.Message}");
        }
    }
}
