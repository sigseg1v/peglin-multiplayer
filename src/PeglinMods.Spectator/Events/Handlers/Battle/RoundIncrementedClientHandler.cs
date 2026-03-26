namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

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
            SpectatorPlugin.Logger.LogWarning($"RoundIncremented handler failed: {e.Message}");
        }
    }
}
