using System;
using System.Reflection;
using global::Battle;
using HarmonyLib;
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class CritActivatedClientHandler : IClientHandler<CritActivatedEvent>
{
    private static readonly FieldInfo CritCountField
        = AccessTools.Field(typeof(BattleController), "_criticalHitCount");

    public void Handle(CritActivatedEvent networkEvent)
    {
        try
        {
            // Host ActivateCrit() increments _criticalHitCount before firing the
            // event. Without mirroring that, BattleController.criticalActive stays
            // false on the client — Reset(false) during refresh heal paints white
            // while the host used Reset(true) → _bonusSprite / Bonus color.
            //
            // This is a responsiveness optimization ONLY. The host clears
            // _criticalHitCount in five places (OnDisable, DoEndOfTurnBattleCleanup,
            // CheckForForcedCritical, EndBattle, ArmNavigationBall) and only one of
            // them fires onCriticalHitDeactivated, so this counter would climb
            // forever on events alone. PegboardStateApplier.ApplyCriticalHitCount overwrites it
            // from PegboardStateSnapshot.CriticalHitCount every heartbeat — that is
            // the authoritative path.
            if (CritCountField != null)
            {
                var n = (int)(CritCountField.GetValue(null) ?? 0);
                CritCountField.SetValue(null, n + 1);
            }

            BattleController.onCriticalHitActivated?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"CritActivated handler failed: {e.Message}");
        }
    }
}
