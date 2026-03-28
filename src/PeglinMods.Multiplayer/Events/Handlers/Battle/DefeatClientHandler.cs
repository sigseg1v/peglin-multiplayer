namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Battle;

public sealed class DefeatClientHandler : IClientHandler<DefeatEvent>
{
    public void Handle(DefeatEvent networkEvent)
    {
        try
        {
            PlayerHealthController.OnDefeat?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"Defeat handler failed: {e.Message}");
        }
    }
}
