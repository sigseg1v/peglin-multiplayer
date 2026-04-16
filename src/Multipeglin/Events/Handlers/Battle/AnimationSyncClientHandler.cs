using System;
using Multipeglin.Events.Network.Battle;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class AnimationSyncClientHandler : IClientHandler<AnimationSyncEvent>
{
    public void Handle(AnimationSyncEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            // Find the entity by GUID — try enemy first, then peg
            Animator animator = null;

            var enemyId = MultiplayerPlugin.Services?.TryResolve<EnemyIdentifier>(out var eid) == true ? eid : null;
            var enemy = enemyId?.Find(e.EntityGuid);
            if (enemy != null)
            {
                animator = enemy.GetComponentInChildren<Animator>();
            }
            else
            {
                var pegId = MultiplayerPlugin.Services?.TryResolve<PegIdentifier>(out var pid) == true ? pid : null;
                var peg = pegId?.Find(e.EntityGuid);
                if (peg != null)
                    animator = peg.GetComponentInChildren<Animator>();
            }

            if (animator == null) return;

            switch (e.ParamType)
            {
                case "trigger":
                    animator.SetTrigger(e.ParamName);
                    break;
                case "bool":
                    animator.SetBool(e.ParamName, e.Value != 0);
                    break;
                case "integer":
                    animator.SetInteger(e.ParamName, e.Value);
                    break;
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"AnimationSync handler failed: {ex.Message}");
        }
    }
}
