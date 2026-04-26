using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

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
