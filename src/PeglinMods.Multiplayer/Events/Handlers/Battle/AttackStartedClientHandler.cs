namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using global::Battle.Attacks;
using PeglinMods.Multiplayer.Events.Network.Battle;
using PeglinMods.Multiplayer.Multiplayer;

public sealed class AttackStartedClientHandler : IClientHandler<AttackStartedEvent>
{
    public void Handle(AttackStartedEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;

            // Fire the BattleController delegate (UI updates, orb scale out, etc.)
            BattleController.OnAttackStarted?.Invoke();

            if (mode != null && mode.IsSpectating && !string.IsNullOrEmpty(e.AnimTrigger))
            {
                // Trigger the peglin attack animation via AttackManager.OnAttackPerformed
                // This makes PeglinBattleAnimationController play the throw animation
                AttackManager.OnAttackPerformed?.Invoke(e.AnimTrigger);

                MultiplayerPlugin.Logger?.LogInfo($"[AttackStarted] Playing attack anim '{e.AnimTrigger}', target={e.TargetEnemyGuid}");
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"AttackStarted handler failed: {ex.Message}");
        }
    }
}
