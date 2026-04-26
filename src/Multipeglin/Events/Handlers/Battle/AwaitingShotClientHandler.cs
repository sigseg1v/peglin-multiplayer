using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

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
