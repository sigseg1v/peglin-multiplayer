namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

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
