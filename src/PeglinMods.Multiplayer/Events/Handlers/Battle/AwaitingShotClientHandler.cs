namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

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
            MultiplayerPlugin.Logger.LogWarning($"AwaitingShot handler failed: {e.Message}");
        }
    }
}
