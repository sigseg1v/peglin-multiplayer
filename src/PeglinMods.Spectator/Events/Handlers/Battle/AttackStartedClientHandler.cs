namespace PeglinMods.Spectator.Events.Handlers.Battle;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Battle;

public sealed class AttackStartedClientHandler : IClientHandler<AttackStartedEvent>
{
    public void Handle(AttackStartedEvent networkEvent)
    {
        try
        {
            BattleController.OnAttackStarted?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"AttackStarted handler failed: {e.Message}");
        }
    }
}
