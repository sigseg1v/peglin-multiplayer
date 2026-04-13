namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

using System;
using global::Battle;
using global::Battle.Attacks;
using PeglinMods.Multiplayer.Events.Network.Battle;
using PeglinMods.Multiplayer.GameState;
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
                // Trigger the peglin attack animation
                AttackManager.OnAttackPerformed?.Invoke(e.AnimTrigger);

                // Set up the projectile to launch when the animation fires OnFirePoint
                ClientAttackProjectile.Instance?.SetupAttack(e.TargetEnemyGuid, e.NumPegsHit, e.IsCrit, e.OrbName);

                MultiplayerPlugin.Logger?.LogInfo($"[AttackStarted] anim='{e.AnimTrigger}', target={e.TargetEnemyGuid}, pegs={e.NumPegsHit}, crit={e.IsCrit}, orb={e.OrbName}");
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"AttackStarted handler failed: {ex.Message}");
        }
    }
}
