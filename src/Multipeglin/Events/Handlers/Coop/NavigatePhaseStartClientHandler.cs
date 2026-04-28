using HarmonyLib;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;
using PeglinUI.PostBattle;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Client handler for NavigatePhaseStartEvent: enters the parallel-shoot navigate
/// phase locally. Sets AllowNavigateLogic, clears all "waiting" overlays, force-
/// closes the BattleUpgradeCanvas left over from the reward phase, and invokes
/// the native nav-arming method (PostBattleController.SkipMimicNavigation for
/// "post_battle"; nav_only is handled host-solo and ignored here).
/// </summary>
public sealed class NavigatePhaseStartClientHandler : IClientHandler<NavigatePhaseStartEvent>
{
    public void Handle(NavigatePhaseStartEvent networkEvent)
    {
        var log = MultiplayerPlugin.Logger;
        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            log?.LogWarning("[CoopNavigate] Client: no Services — skipping phase start");
            return;
        }

        // Host runs the resolver locally; this handler only fires on clients.
        if (services.TryResolve<IMultiplayerMode>(out var mode) && mode.IsHosting)
        {
            return;
        }

        log?.LogInfo(
            $"[CoopNavigate] === Client received navigate phase start: source={networkEvent.Source}, children={networkEvent.ChildNodeCount} ===");

        // Defensive: nav_only sources (treasure/shop/text/peg-minigame) are
        // host-solo by contract — the host runs the nav-shot natively after
        // AllChoicesComplete and the scene transitions. Clients do NOT have
        // NavOnlyController properly wired (no chest Skip / shop CloseStore
        // ran locally) so arming a nav ball here NREs. If a stray nav_only
        // event arrives anyway (older host build / out-of-order delivery),
        // log it and stay in the awaiting-host overlay; the map sync will
        // pull us onto the next scene shortly.
        if (networkEvent.Source == "nav_only")
        {
            log?.LogWarning(
                "[CoopNavigate] Ignoring nav_only phase start on client — host runs nav-shot solo by lockstep contract");
            return;
        }

        CoopNavigateState.StartPhase(
            networkEvent.Source,
            networkEvent.ChildNodeCount,
            totalVoters: 1, // client only knows about itself; not used for resolution on client
            now: Time.unscaledTime);

        Patches.MultiplayerClientPatches.AllowNavigateLogic = true;

        // Drop every "waiting for X" flag so neither the CoopRewardUI black
        // overlay nor the TurnIndicator banner stays up. The reward phase is
        // over — the player needs a clean playfield to aim at.
        CoopRewardState.WaitingForOtherPlayers = false;
        CoopRewardState.AllChoicesComplete = true;
        CoopRewardState.ClientInNativeRewardPhase = false;
        TurnChangeClientHandler.TurnMessage = string.Empty;
        log?.LogInfo("[CoopNavigate] Cleared WaitingForOtherPlayers / TurnMessage / ClientInNativeRewardPhase");

        // Force-close the BattleUpgradeCanvas if it's still up. The native
        // OnBattleUpgradeOver delegate already SetActive(false)'d it before
        // PostBattleController.OnUpgradeFinished -> StartNavigation fires,
        // but if anything held it open the player would see a black panel
        // instead of the playfield.
        ForceCloseBattleUpgradeCanvas(log);

        // Snapshot before-state so we can diagnose if the nav ball doesn't arm.
        LogBattleControllerState(log, "pre-SkipMimicNavigation");

        try
        {
            InvokePostBattleStartNavigation(log);
        }
        catch (System.Exception ex)
        {
            log?.LogError(
                $"[CoopNavigate] Client failed to arm nav ball: {ex.Message}\n{ex.StackTrace}");
        }

        LogBattleControllerState(log, "post-SkipMimicNavigation");

        // Verify async (next frame) that the ball actually got armed. Coroutine
        // host: any active MonoBehaviour. We piggyback on MultiplayerPlugin's
        // existing manager object via a one-shot helper.
        ScheduleVerifyArmed(log);
    }

    private static void InvokePostBattleStartNavigation(BepInEx.Logging.ManualLogSource log)
    {
        var pbcs = Resources.FindObjectsOfTypeAll<global::Battle.PostBattleController>();
        global::Battle.PostBattleController target = null;
        if (pbcs != null)
        {
            foreach (var p in pbcs)
            {
                if (p != null && p.gameObject != null && p.gameObject.activeInHierarchy)
                {
                    target = p;
                    break;
                }
            }

            if (target == null && pbcs.Length > 0)
            {
                target = pbcs[0];
            }
        }

        if (target == null)
        {
            log?.LogWarning("[CoopNavigate] Client: no PostBattleController to start navigation on");
            return;
        }

        log?.LogInfo(
            $"[CoopNavigate] Found PostBattleController '{target.name}' (active={target.gameObject.activeInHierarchy}) — invoking SkipMimicNavigation");

        // Make sure the PBC GameObject is active. PostBattleStartClientHandler
        // activated it earlier for the reward UI; if anything deactivated it
        // we re-activate here so OnEnable's SerializeField hookups are valid.
        if (!target.gameObject.activeInHierarchy)
        {
            target.gameObject.SetActive(true);
        }

        // Use SkipMimicNavigation: it calls private StartNavigation(movePeglin:false)
        // and then BattleController.ArmNavigationBall() synchronously. The default
        // StartNavigation(true) path relies on a DOTween player-move whose
        // OnComplete fires MoveFinished → ArmNavigationBall — this tween chain
        // is unreliable on the client (BattleController.Update is fully blocked,
        // and the player transform is at its rest position so the tween becomes
        // a 0-duration no-op whose onComplete never fires in some DOTween
        // versions). Result: nav ball was never armed, clients sat on black
        // screens with only the heartbeat-synced aimer sprite. Bypassing the
        // tween and arming the ball directly fixes that.
        target.SkipMimicNavigation();
        log?.LogInfo("[CoopNavigate] Client invoked PostBattleController.SkipMimicNavigation");
    }

    private static void ForceCloseBattleUpgradeCanvas(BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            var canvases = Resources.FindObjectsOfTypeAll<BattleUpgradeCanvas>();
            if (canvases == null)
            {
                return;
            }

            foreach (var canvas in canvases)
            {
                if (canvas != null && canvas.gameObject != null && canvas.gameObject.activeInHierarchy)
                {
                    canvas.gameObject.SetActive(false);
                    log?.LogInfo($"[CoopNavigate] Force-closed BattleUpgradeCanvas '{canvas.name}'");
                }
            }
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] ForceCloseBattleUpgradeCanvas failed: {ex.Message}");
        }
    }

    private static void LogBattleControllerState(BepInEx.Logging.ManualLogSource log, string label)
    {
        try
        {
            var bc = Object.FindObjectOfType<global::Battle.BattleController>();
            if (bc == null)
            {
                log?.LogWarning($"[CoopNavigate] [{label}] BattleController is null");
                return;
            }

            var activeBallField = AccessTools.Field(typeof(global::Battle.BattleController), "_activePachinkoBall");
            var activeBall = activeBallField?.GetValue(bc) as GameObject;
            var navOrbField = AccessTools.Field(typeof(global::Battle.BattleController), "_navigationOrb");
            var navOrbPrefab = navOrbField?.GetValue(bc) as GameObject;
            var rmField = AccessTools.Field(typeof(global::Battle.BattleController), "_relicManager");
            var dmField = AccessTools.Field(typeof(global::Battle.BattleController), "_deckManager");
            var pmField = AccessTools.Field(typeof(global::Battle.BattleController), "_predictionManager");

            var bcState = global::Battle.BattleController.CurrentBattleState;
            log?.LogInfo(
                $"[CoopNavigate] [{label}] BC state={bcState} " +
                $"activeBall={(activeBall != null ? $"alive(active={activeBall.activeInHierarchy})" : "null")} " +
                $"navOrbPrefab={(navOrbPrefab != null ? navOrbPrefab.name : "NULL")} " +
                $"relicMgr={(rmField?.GetValue(bc) != null ? "ok" : "NULL")} " +
                $"deckMgr={(dmField?.GetValue(bc) != null ? "ok" : "NULL")} " +
                $"predMgr={(pmField?.GetValue(bc) != null ? "ok" : "NULL")} " +
                $"spawn={bc.pachinkoBallSpawnLocation}");
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] [{label}] state-log failed: {ex.Message}");
        }
    }

    private static void ScheduleVerifyArmed(BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            var go = new GameObject("CoopNavVerify")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            Object.DontDestroyOnLoad(go);
            var helper = go.AddComponent<NavBallArmVerifier>();
            helper.Log = log;
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] ScheduleVerifyArmed failed: {ex.Message}");
        }
    }

    private sealed class NavBallArmVerifier : MonoBehaviour
    {
        public BepInEx.Logging.ManualLogSource Log;
        private float _t;
        private int _retries;

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (_t < 0.25f)
            {
                return;
            }

            _t = 0f;
            try
            {
                var bc = FindObjectOfType<global::Battle.BattleController>();
                if (bc == null)
                {
                    return;
                }

                var activeBallField = AccessTools.Field(typeof(global::Battle.BattleController), "_activePachinkoBall");
                var activeBall = activeBallField?.GetValue(bc) as GameObject;
                if (activeBall != null && activeBall.activeInHierarchy)
                {
                    var pb = activeBall.GetComponent<PachinkoBall>();
                    Log?.LogInfo(
                        $"[CoopNavigate] Verify: nav ball alive at {activeBall.transform.position} " +
                        $"state={(pb != null ? pb.CurrentState.ToString() : "noPB")}");
                    Destroy(gameObject);
                    return;
                }

                _retries++;
                if (_retries == 1 || _retries % 8 == 0)
                {
                    Log?.LogWarning(
                        $"[CoopNavigate] Verify: nav ball still not armed after {_retries * 0.25f:F2}s — retrying ArmNavigationBall");
                    try
                    {
                        bc.ArmNavigationBall();
                    }
                    catch (System.Exception ex)
                    {
                        Log?.LogError($"[CoopNavigate] Verify: ArmNavigationBall threw: {ex.Message}");
                    }
                }

                if (_retries >= 24) // ~6s
                {
                    Log?.LogError("[CoopNavigate] Verify: gave up after 6s — nav ball never armed on client");
                    Destroy(gameObject);
                }
            }
            catch (System.Exception ex)
            {
                Log?.LogError($"[CoopNavigate] Verify Update failed: {ex.Message}");
                Destroy(gameObject);
            }
        }
    }
}
