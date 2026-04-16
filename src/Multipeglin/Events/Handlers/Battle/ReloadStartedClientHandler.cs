namespace Multipeglin.Events.Handlers.Battle;

using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;

public sealed class ReloadStartedClientHandler : IClientHandler<ReloadStartedEvent>
{
    public void Handle(ReloadStartedEvent networkEvent)
    {
        try
        {
            BattleController.OnReloadStarted?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"ReloadStarted handler failed: {e.Message}");
        }
    }
}
