
using System;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;

namespace Multipeglin.Events.Handlers.Coop;
/// <summary>
/// On host: receives a client's target selection and toggles a visual
/// targeting indicator on the selected enemy. This lets the host see
/// which enemy the client is targeting in real-time.
/// </summary>
public sealed class TargetSelectClientHandler : IClientHandler<TargetSelectEvent>
{
    /// <summary>The enemy currently highlighted as the client's target.</summary>
    private static global::Battle.Enemies.Enemy _currentClientTarget;

    /// <summary>The GUID of the client's currently selected target.</summary>
    public static string CurrentClientTargetGuid { get; private set; }

    public void Handle(TargetSelectEvent networkEvent)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsHosting)
            {
                return;
            }

            // Clear previous highlight
            if (_currentClientTarget != null)
            {
                try
                { _currentClientTarget.ToggleTargetedUI(on: false); }
                catch { }

                _currentClientTarget = null;
            }

            CurrentClientTargetGuid = networkEvent.TargetEnemyGuid;

            if (string.IsNullOrEmpty(networkEvent.TargetEnemyGuid))
            {
                return;
            }

            var enemyId = MultiplayerPlugin.Services?.TryResolve<EnemyIdentifier>(out var eid) == true ? eid : null;
            if (enemyId == null)
            {
                return;
            }

            var enemy = enemyId.Find(networkEvent.TargetEnemyGuid);
            if (enemy == null || enemy.CurrentHealth <= 0f)
            {
                return;
            }

            // Show targeting UI on the client's selected enemy
            enemy.ToggleTargetedUI(on: true);
            _currentClientTarget = enemy;
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[TargetSelect] Handler failed: {ex.Message}");
        }
    }

    /// <summary>Clear the client target indicator (e.g. when turn changes).</summary>
    public static void ClearClientTarget()
    {
        if (_currentClientTarget != null)
        {
            try
            { _currentClientTarget.ToggleTargetedUI(on: false); }
            catch { }

            _currentClientTarget = null;
        }

        CurrentClientTargetGuid = null;
    }
}
