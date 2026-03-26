namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

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
            SpectatorPlugin.Logger.LogWarning($"ReloadStarted handler failed: {e.Message}");
        }
    }
}
