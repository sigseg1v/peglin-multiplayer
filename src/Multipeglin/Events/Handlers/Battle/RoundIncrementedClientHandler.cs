using System;
using global::Battle;
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class RoundIncrementedClientHandler : IClientHandler<RoundIncrementedEvent>
{
    public void Handle(RoundIncrementedEvent networkEvent)
    {
        try
        {
            BattleController.OnRoundCountIncremented?.Invoke(networkEvent.RoundCount);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"RoundIncremented handler failed: {e.Message}");
        }
    }
}
