using System;
using global::Battle;
using Multipeglin.Events.Network.Health;

namespace Multipeglin.Events.Handlers.Health;

public sealed class DodgeClientHandler : IClientHandler<DodgeEvent>
{
    public void Handle(DodgeEvent networkEvent)
    {
        try
        {
            PlayerHealthController.OnDodge?.Invoke(networkEvent.DodgeInfo);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"Dodge handler failed: {e.Message}");
        }
    }
}
