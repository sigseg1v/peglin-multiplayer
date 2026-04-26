using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class TargetingManagerPatches
{
    // =========================================================================
    // CLIENT TARGET SELECTION — send TargetSelectEvent when client changes target
    // =========================================================================

    /// <summary>
    /// When the client selects an enemy target during their aiming phase, send a
    /// TargetSelectEvent to the host so the host can display the targeting indicator.
    /// </summary>
    [HarmonyPatch(typeof(Battle.TargetingManager), "SetEnemyAsTarget")]
    [HarmonyPostfix]
    public static void TargetingManager_SetEnemyAsTarget_Postfix(Battle.Enemies.Enemy enemy)
    {
        if (!ShouldSuppressClientLogic)
        {
            return;
        }

        if (!UI.LobbyUI.GameStartReceived)
        {
            return;
        }

        if (!Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn)
        {
            return;
        }

        try
        {
            string guid = null;
            if (enemy != null)
            {
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Utility.EnemyIdentifier>(out var eid) == true)
                {
                    guid = eid.GetGuid(enemy);
                }
            }

            var services2 = MultiplayerPlugin.Services;
            if (services2?.TryResolve<Network.IMessageSender>(out var sender) == true)
            {
                sender.Send(new Events.Network.Coop.TargetSelectEvent
                {
                    TargetEnemyGuid = guid,
                });
            }
        }
        catch { }
    }
}
