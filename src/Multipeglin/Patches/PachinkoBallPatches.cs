using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class PachinkoBallPatches
{
    // =========================================================================
    // CLIENT SHOT INTERCEPTION — send ShootRequestEvent to host in coop
    // =========================================================================

    /// <summary>
    /// In coop, when the client fires their shot, capture the aim direction and
    /// send a ShootRequestEvent to the host. The local fire is allowed to proceed
    /// so the client sees their own shot immediately.
    /// </summary>
    [HarmonyPatch(typeof(PachinkoBall), "Fire")]
    [HarmonyPrefix]
    public static bool PachinkoBall_Fire_Prefix(PachinkoBall __instance)
    {
        // HOST-SIDE: block the host player from firing during a client's turn.
        // Only allow Fire() when ExecutingPendingShot is set (our postfix programmatic fire).
        if (IsHosting && UI.LobbyUI.GameStartReceived && !ExecutingPendingShot)
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<GameState.TurnManager>(out var tm) == true
                && tm.CurrentPlayerSlot > 0) // slot 0 = host, >0 = client's turn
            {
                return false; // Silently block — host can't fire during client turns
            }
        }

        // CLIENT-SIDE: intercept Fire() to capture aim and send ShootRequest to host.
        // PachinkoBall.Update() runs natively on client for aiming. When the player
        // clicks, it calls Fire(). We capture the aim direction and send it to host.
        // Exception: during PegMinigame, the client fires independently.
        if (ShouldSuppressClientLogic)
        {
            if (AllowPegMinigameLogic)
            {
                return true; // Let the client fire normally in PegMinigame
            }

            if (UI.LobbyUI.GameStartReceived
                && Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn
                && !ClientShotSentThisTurn)
            {
                var aimVec = __instance.aimVector;
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
                {
                    // Capture the client's selected target enemy GUID
                    string targetGuid = null;
                    try
                    {
                        var targetMgr = UnityEngine.Object.FindObjectOfType<Battle.TargetingManager>();
                        if (targetMgr?.currentTarget != null &&
                            services.TryResolve<Utility.EnemyIdentifier>(out var enemyId))
                        {
                            targetGuid = enemyId.GetGuid(targetMgr.currentTarget);
                        }
                    }
                    catch { }

                    sender.Send(new Events.Network.Coop.ShootRequestEvent
                    {
                        AimDirectionX = aimVec.x,
                        AimDirectionY = aimVec.y,
                        TargetEnemyGuid = targetGuid,
                    });
                    ClientShotSentThisTurn = true;

                    // Clean up prediction visuals — Fire() normally calls
                    // _predictionManager.PlayerFired() but we're blocking Fire().
                    var pmField = HarmonyLib.AccessTools.Field(typeof(PachinkoBall), "_predictionManager");
                    var pm = pmField?.GetValue(__instance) as PredictionManager;
                    try
                    { pm?.PlayerFired(); }
                    catch { }

                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[ClientPatches] Fire intercepted → ShootRequest: aim=({aimVec.x:F2},{aimVec.y:F2}), target={targetGuid ?? "auto"}");
                }
            }

            return false;
        }

        return true; // Non-multiplayer: allow
    }

    /// <summary>
    /// Host-side: track the primary fired ball so HostBallRegistry/EnsureBallRegistered
    /// can skip it (primary ball is synced via BallPositionEvent, not MultiballSpawnedEvent).
    /// Runs for both host's own shots and client-delegated shots.
    /// </summary>
    [HarmonyPatch(typeof(PachinkoBall), "Fire")]
    [HarmonyPostfix]
    public static void PachinkoBall_Fire_Postfix(PachinkoBall __instance)
    {
        if (!IsHosting)
        {
            return;
        }

        if (__instance == null || __instance.IsDummy)
        {
            return;
        }

        _firedBallGO = __instance.gameObject;
        _firedBallTimer = 0f;
        _firedBallLogCount = 0;
    }
}
