using System;
using System.Reflection;
using global::Battle;
using HarmonyLib;
using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class CritDeactivatedClientHandler : IClientHandler<CritDeactivatedEvent>
{
    private static readonly FieldInfo CritCountField
        = AccessTools.Field(typeof(BattleController), "_criticalHitCount");

    public void Handle(CritDeactivatedEvent networkEvent)
    {
        try
        {
            // Host zeros the count before firing onCriticalHitDeactivated.
            CritCountField?.SetValue(null, 0);
            BattleController.onCriticalHitDeactivated?.Invoke();
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"CritDeactivated handler failed: {e.Message}");
        }
    }
}
