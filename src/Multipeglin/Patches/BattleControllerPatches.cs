using System;
using System.Collections;
using Battle;
using HarmonyLib;
using Multipeglin.Events;
using Multipeglin.Events.Subscriptions;
using PeglinUI;
using UnityEngine;
using static Multipeglin.Patches.MultiplayerClientPatches;
using Random = UnityEngine.Random;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class BattleControllerPatches
{
    [HarmonyPatch(typeof(BattleController), "Update")]
    [HarmonyPrefix]
    public static bool BattleController_Update_Prefix(BattleController __instance)
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        var isMyTurn = Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn;
        var shotSent = ClientShotSentThisTurn;

        // In co-op: handle aiming input ourselves instead of running BattleController.Update.
        // BattleController.Update in AWAITING_SHOT fires OnStartedAwaitingShot, increments
        // round count, resets per-shot relics, etc. — side effects that corrupt client state.
        // HandleClientAiming creates the ball visual needed for the native prediction aimer.
        if (isMyTurn && !shotSent)
        {
            // Safety: if ball was destroyed (scene change) but flag stayed, reset
            if (_clientBallInitialized && _clientBallGO == null)
            {
                MultiplayerPlugin.Logger?.LogWarning("[ClientAim] Ball was destroyed externally — resetting for retry");
                _clientBallInitialized = false;
            }

            HandleClientAiming(__instance);

            // Send aim direction to host at 10 Hz so the host sees the client's aim line
            if (_clientBallInitialized && _clientBallGO != null)
            {
                _clientAimSendTimer += UnityEngine.Time.unscaledDeltaTime;
                if (_clientAimSendTimer >= ClientAimSendInterval)
                {
                    _clientAimSendTimer = 0f;
                    SendClientAimUpdate();
                }
            }

            // Right-click or Backspace to discard orb — send request to host.
            // Backspace is the default Rewired "Back" action the native OrbDiscardButton
            // listens for; we mirror that here because the native path is blocked on
            // the client (discards route through OrbDiscardRequestEvent to host).
            if (_clientBallInitialized && !_clientDiscardSentThisTurn
                && (UnityEngine.Input.GetMouseButtonDown(1)
                    || UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Backspace)))
            {
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
                {
                    sender.Send(new Events.Network.Coop.OrbDiscardRequestEvent());
                    _clientDiscardSentThisTurn = true;
                    MultiplayerPlugin.Logger?.LogInfo("[ClientAim] Sent OrbDiscardRequest to host");
                }
            }

            // Direct left-click to fire — fallback for when PachinkoBall.LateUpdate's
            // native click→Fire path breaks. Observed in MirrorPlantBattle: layout has
            // raycast-blocking sprites that make IsPointerOverGameObject() permanently
            // true over the playfield, so PlayfieldMouseDetector.OnPointerDown never
            // fires and Fire() is never called — soft-locks the client. Gate by
            // IsPointerOverInteractiveUI() instead so non-button UI overlays don't
            // block the shot, while Fire/Skip/Discard buttons still suppress it.
            // Also gate on IsPointerOverEnemy(): clicking an enemy is target-selection,
            // NOT a fire intent — firing on target-click annoys the player.
            // Idempotent: Fire prefix sets ClientShotSentThisTurn first if it runs.
            if (_clientBallInitialized && _clientBallGO != null
                && !ClientShotSentThisTurn
                && UnityEngine.Input.GetMouseButtonDown(0)
                && !IsPointerOverInteractiveUI()
                && !IsPointerOverEnemy())
            {
                TrySendDirectShot();
            }
        }
        else
        {
            // Not aiming — clean up client ball and trajectory
            if (_clientBallInitialized)
            {
                CleanupClientAiming();
            }

            _clientAimSendTimer = 0f;
        }

        // Per-second diagnostic: log client aiming state (throttled)
        _clientAimDiagTimer += UnityEngine.Time.unscaledDeltaTime;
        if (_clientAimDiagTimer >= 2f)
        {
            _clientAimDiagTimer = 0f;
            var ballState = "none";
            if (_clientBallGO != null)
            {
                var ball = _clientBallGO.GetComponent<PachinkoBall>();
                ballState = ball != null ? ball.CurrentState.ToString() : "noPB";
            }
            else if (_clientBallInitialized)
            {
                ballState = "destroyed";
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientAim/Diag] isMyTurn={isMyTurn} shotSent={shotSent} ballInit={_clientBallInitialized} " +
                $"ballState={ballState} ballGO={(_clientBallGO != null ? "alive" : "null")} " +
                $"blocking={GameBlockingWindow.wasOpenThisFrame} preBattlePause={BattleController.PreBattlePause}");
        }

        return false; // Always block BattleController.Update on the client
    }

    /// <summary>
    /// Host-side: when it's a client's turn and we have a PendingShot from
    /// ShootRequestEvent, set the aim vector on the PachinkoBall and fire it.
    /// This runs after BattleController.Update on the host.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "Update")]
    [HarmonyPostfix]
    public static void BattleController_Update_Postfix()
    {
        if (!IsHosting)
        {
            return;
        }

        if (!UI.LobbyUI.GameStartReceived)
        {
            return;
        }

        // Track fired ball position to diagnose collision issues
        if (_firedBallGO != null && _firedBallLogCount < 5)
        {
            _firedBallTimer += UnityEngine.Time.deltaTime;
            if (_firedBallTimer >= 0.5f * (_firedBallLogCount + 1))
            {
                _firedBallLogCount++;
                var rb = _firedBallGO.GetComponent<UnityEngine.Rigidbody2D>();
                var ball = _firedBallGO.GetComponent<PachinkoBall>();
                if (rb != null)
                {
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[BallTrack] t={_firedBallTimer:F1}s pos=({_firedBallGO.transform.position.x:F1},{_firedBallGO.transform.position.y:F1}), " +
                        $"vel=({rb.velocity.x:F1},{rb.velocity.y:F1}), sim={rb.simulated}, bodyType={rb.bodyType}, " +
                        $"state={ball?.CurrentState}, active={_firedBallGO.activeInHierarchy}");
                }
                else
                {
                    MultiplayerPlugin.Logger?.LogInfo($"[BallTrack] t={_firedBallTimer:F1}s ball destroyed or rb null");
                    _firedBallGO = null;
                }
            }
        }

        // ---------------------------------------------------------------
        // Stuck AWAITING_SHOT_COMPLETION watchdog. _remainingPachinkoBalls
        // can stay > 0 forever when satellite balls (e.g. SummoningCircle ->
        // a custom orb caught in Spirit-of-Radia black-hole gravity) never
        // trip PachinkoBall.Update's auto-destroy heuristics — turn manager
        // hangs, every client sits in (shot) heartbeats with no progress.
        // After ShotFired we wait until at least 5s of "no live FIRING ball"
        // is sustained, then force-zero the counter so OnShotComplete fires.
        // ---------------------------------------------------------------
        if (BattleController.CurrentBattleState == BattleController.BattleState.AWAITING_SHOT_COMPLETION)
        {
            var bcInstance = UnityEngine.Object.FindObjectOfType<BattleController>();
            if (bcInstance != null)
            {
                var remainingField = HarmonyLib.AccessTools.Field(typeof(BattleController), "_remainingPachinkoBalls");
                var counter = remainingField != null ? (int)remainingField.GetValue(bcInstance) : 0;
                var elapsedSinceShot = UnityEngine.Time.unscaledTime - _shotFiredUnscaledTime;

                // Don't engage the watchdog inside the SC spawn window — its
                // FireOrbs coroutine takes ~0.5s × 16 + 1s = ~9s to finish
                // dispatching satellites, and during the gaps between yields
                // the live-firing count may briefly drop to zero even though
                // the shot is progressing normally.
                if (counter > 0 && elapsedSinceShot >= 10f)
                {
                    _stuckCompletionScanTimer += UnityEngine.Time.unscaledDeltaTime;
                    if (_stuckCompletionScanTimer >= 0.5f)
                    {
                        _stuckCompletionScanTimer = 0f;

                        var allBalls = UnityEngine.Object.FindObjectsOfType<PachinkoBall>();
                        var firingCount = 0;
                        for (var i = 0; i < allBalls.Length; i++)
                        {
                            var b = allBalls[i];
                            if (b != null && !b.IsDummy && b.IsFiring())
                            {
                                firingCount++;
                            }
                        }

                        if (firingCount == 0)
                        {
                            if (_stuckCompletionSinceUnscaledTime == 0f)
                            {
                                _stuckCompletionSinceUnscaledTime = UnityEngine.Time.unscaledTime;
                                MultiplayerPlugin.Logger?.LogWarning(
                                    $"[ShotWatchdog] AWAITING_SHOT_COMPLETION counter={counter} but no live firing balls (elapsed={elapsedSinceShot:F1}s) — arming unstick timer");
                            }
                            else if (UnityEngine.Time.unscaledTime - _stuckCompletionSinceUnscaledTime >= 15f)
                            {
                                MultiplayerPlugin.Logger?.LogWarning(
                                    $"[ShotWatchdog] Stuck for >15s with no live firing balls — force-zeroing _remainingPachinkoBalls (was {counter}) to release the turn");

                                // Belt-and-suspenders: also call StartDestroy on
                                // any non-dummy balls still in scene that aren't
                                // already AWAITING_RESULTS. They'd decrement the
                                // counter via OnPachinkoBallDestroyed, but since
                                // we're about to zero the field directly that's
                                // benign — we just don't want them lingering as
                                // active physics objects in the next turn.
                                for (var i = 0; i < allBalls.Length; i++)
                                {
                                    var b = allBalls[i];
                                    if (b != null && !b.IsDummy && b.CurrentState != PachinkoBall.FireballState.AWAITING_RESULTS)
                                    {
                                        try
                                        {
                                            b.StartDestroy();
                                        }
                                        catch (System.Exception ex)
                                        {
                                            MultiplayerPlugin.Logger?.LogWarning(
                                                $"[ShotWatchdog] StartDestroy on lingering ball failed: {ex.Message}");
                                        }
                                    }
                                }

                                remainingField?.SetValue(bcInstance, 0);
                                _stuckCompletionSinceUnscaledTime = 0f;
                            }
                        }
                        else
                        {
                            _stuckCompletionSinceUnscaledTime = 0f;
                        }
                    }
                }
                else
                {
                    _stuckCompletionSinceUnscaledTime = 0f;
                    _stuckCompletionScanTimer = 0f;
                }
            }
        }
        else
        {
            _stuckCompletionSinceUnscaledTime = 0f;
            _stuckCompletionScanTimer = 0f;
        }

        // Only process when BattleController is in AWAITING_SHOT
        if (BattleController.CurrentBattleState != BattleController.BattleState.AWAITING_SHOT)
        {
            return;
        }

        // Handle pending discard request from client before checking for shots
        if (Events.Handlers.Coop.OrbDiscardRequestClientHandler.PendingDiscard)
        {
            Events.Handlers.Coop.OrbDiscardRequestClientHandler.PendingDiscard = false;

            var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
            if (bc != null)
            {
                // The client's deck is loaded into singletons during their turn.
                // AttemptOrbDiscard reads from those singletons, so it operates on the client's deck.
                // We need to temporarily re-activate the ball so AttemptOrbDiscard sees it as available.
                var activeBallField = HarmonyLib.AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
                var ballGO = activeBallField?.GetValue(bc) as UnityEngine.GameObject;
                var wasInactive = ballGO != null && !ballGO.activeInHierarchy;
                if (wasInactive)
                {
                    ballGO.SetActive(true);
                }

                // Clear populatingDisplayOrb — the previous discard's DrawBall may have
                // started a DeckInfoManager animation that hasn't finished yet. This flag
                // blocks AttemptOrbDiscard, preventing relics like Ambidextionary (2 discards)
                // from working. Also disconnect DeckManager.onBallUsed so the client's DrawBall
                // doesn't trigger the host's deck tube animation.
                DeckInfoManager.populatingDisplayOrb = false;
                var savedOnBallUsed = DeckManager.onBallUsed;
                DeckManager.onBallUsed = _ => { };

                _executingPendingDiscard = true;
                try
                {
                    var discardMethod = HarmonyLib.AccessTools.Method(typeof(BattleController), "AttemptOrbDiscard");
                    discardMethod?.Invoke(bc, null);
                }
                finally
                {
                    _executingPendingDiscard = false;
                    DeckManager.onBallUsed = savedOnBallUsed;
                }

                // If the discard emptied the shuffled deck, AttemptOrbDiscard calls
                // StartReloading() which sets _skipPlayerTurnCount. In coop, this would
                // skip the client's turn and leave the host with no ball to fire — causing
                // the pending shot to never execute. Clear the skip count and force the
                // state back to AWAITING_SHOT so the reload + DrawBall cycle completes.
                var skipField = HarmonyLib.AccessTools.Field(typeof(BattleController), "_skipPlayerTurnCount");
                if (skipField != null)
                {
                    var skipCount = (int)skipField.GetValue(bc);
                    if (skipCount > 0)
                    {
                        skipField.SetValue(bc, 0);
                        MultiplayerPlugin.Logger?.LogInfo(
                            $"[ClientPatches] Cleared _skipPlayerTurnCount={skipCount} after client discard (reload triggered)");
                    }
                }

                MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Executed pending OrbDiscard for client");
                // AttemptOrbDiscard calls DrawBall internally, DrawBall_Postfix will hide the new ball
            }

            return;
        }

        var pending = Events.Handlers.Coop.ShootRequestClientHandler.PeekPendingShot();
        if (pending == null)
        {
            return;
        }

        // Verify the pending shot is for the currently active player slot.
        // In coop, turns rotate between players — a stale shot from a previous
        // turn must not fire during the wrong player's turn.
        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.TurnManager>(out var tm) == true)
        {
            if (pending.SlotIndex != tm.CurrentPlayerSlot)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[ClientPatches] PendingShot slot mismatch: shot.slot={pending.SlotIndex} current={tm.CurrentPlayerSlot} — discarding");
                return;
            }
        }

        try
        {
            // Get BattleController's _activePachinkoBall directly via reflection.
            // DrawBall creates the ball with a scale animation — ArmBallForShot only
            // fires when the animation completes, setting state to AIMING. Scanning
            // for AIMING balls would miss it during the animation.
            var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
            if (bc == null)
            {
                return;
            }

            var activeBallField = HarmonyLib.AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
            var activeBallGO = activeBallField?.GetValue(bc) as UnityEngine.GameObject;
            if (activeBallGO == null)
            {
                // Recovery path: _activePachinkoBall going null mid-coop is almost
                // always a symptom of DrawBall throwing partway through (the typical
                // culprit is DeckInfoManager.PersistBallDrawFinished walking a stale
                // _nextOrb after a slot swap and crashing inside Attack.GetNameWithLevel).
                // Without recovery, the client's pending shot retries forever and
                // the game softlocks. We track the stuck state and re-invoke
                // DrawBall after a short hold, with the deck-tube callbacks
                // disconnected so the same crash path is short-circuited.
                var nowT = UnityEngine.Time.unscaledTime;
                if (_stuckPendingShotSlot != pending.SlotIndex)
                {
                    _stuckPendingShotSlot = pending.SlotIndex;
                    _stuckPendingShotSinceUnscaledTime = nowT;
                    _stuckPendingShotRedraws = 0;
                    _stuckPendingShotLastWarnTime = 0f;
                }

                if (nowT - _stuckPendingShotLastWarnTime >= 1f)
                {
                    _stuckPendingShotLastWarnTime = nowT;
                    MultiplayerPlugin.Logger?.LogWarning(
                        $"[ClientPatches] PendingShot from {pending.PlayerName} but _activePachinkoBall is null " +
                        $"(stuck for {nowT - _stuckPendingShotSinceUnscaledTime:F1}s, retries={_stuckPendingShotRedraws})");
                }

                var stuckFor = nowT - _stuckPendingShotSinceUnscaledTime;
                if (stuckFor >= 1.5f && _stuckPendingShotRedraws < 3)
                {
                    _stuckPendingShotRedraws++;
                    _stuckPendingShotSinceUnscaledTime = nowT;

                    var savedOnBallUsed = DeckManager.onBallUsed;
                    var savedOnDeckShuffled = DeckManager.onDeckShuffled;
                    var savedOnPersistBallUsed = DeckManager.onPersistBallUsed;
                    DeckManager.onBallUsed = _ => { };
                    DeckManager.onDeckShuffled = _ => { };
                    DeckManager.onPersistBallUsed = () => { };
                    DeckInfoManager.populatingDisplayOrb = false;
                    try
                    {
                        var drawBallMethod = HarmonyLib.AccessTools.Method(typeof(BattleController), "DrawBall");
                        drawBallMethod?.Invoke(bc, null);
                        MultiplayerPlugin.Logger?.LogWarning(
                            $"[ClientPatches] Retried DrawBall for stuck slot {pending.SlotIndex} (attempt {_stuckPendingShotRedraws})");
                    }
                    catch (Exception drawEx)
                    {
                        MultiplayerPlugin.Logger?.LogWarning(
                            $"[ClientPatches] DrawBall retry failed for slot {pending.SlotIndex}: {drawEx.InnerException?.Message ?? drawEx.Message}");
                    }
                    finally
                    {
                        DeckManager.onBallUsed = savedOnBallUsed;
                        DeckManager.onDeckShuffled = savedOnDeckShuffled;
                        DeckManager.onPersistBallUsed = savedOnPersistBallUsed;
                    }
                }

                return;
            }

            // Ball recovered (or never went null) — clear stuck-state tracking.
            if (_stuckPendingShotSlot != -1)
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[ClientPatches] Stuck pending shot recovered for slot {_stuckPendingShotSlot} after {_stuckPendingShotRedraws} retry(ies)");
                _stuckPendingShotSlot = -1;
                _stuckPendingShotRedraws = 0;
                _stuckPendingShotLastWarnTime = 0f;
            }

            var activeBall = activeBallGO.GetComponent<PachinkoBall>();
            if (activeBall == null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] PendingShot from {pending.PlayerName} but _activePachinkoBall has no PachinkoBall component");
                return;
            }

            // Kill any scale animation and snap to full size.
            // DrawBall starts a DOScale FROM 0.05 TO the ball's natural scale.
            // If we fire mid-animation, the ball is tiny and its collider passes
            // through pegs without triggering OnCollisionEnter2D.
            // Do this regardless of CurrentState — ArmBallForShot may have already
            // changed it to AIMING, but the scale tween could still be running.
            {
                var tweens = DG.Tweening.DOTween.TweensByTarget(activeBallGO.transform);
                var targetScale = activeBallGO.transform.localScale;
                var hadTween = false;
                if (tweens != null)
                {
                    foreach (var t in tweens)
                    {
                        if (t is DG.Tweening.Core.TweenerCore<UnityEngine.Vector3, UnityEngine.Vector3, DG.Tweening.Plugins.Options.VectorOptions> scaleTween)
                        {
                            targetScale = scaleTween.endValue;
                            hadTween = true;
                            break;
                        }
                    }
                }

                DG.Tweening.DOTween.Kill(activeBallGO.transform);
                // If no tween found but scale is tiny (< 0.1), force to reasonable scale
                if (!hadTween && targetScale.x < 0.1f)
                {
                    targetScale = new UnityEngine.Vector3(0.32f, 0.32f, 0.32f);
                }

                activeBallGO.transform.localScale = targetScale;
                MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Scale snap: ({targetScale.x:F2},{targetScale.y:F2}) hadTween={hadTween} state={activeBall.CurrentState}");
            }

            // InitializeMembers() sets _rigid, _wallbounceAudioSource, _mainCamera etc.
            // It normally runs in Start() (next frame) but we need it NOW before Fire().
            // MUST be called BEFORE setting _aimVector because it calls InitAimVector()
            // which overwrites _aimVector with a default value.
            activeBall.InitializeMembers();

            // The ball created by DrawBall is marked as Dummy (for trajectory prediction).
            // Dummy balls don't process peg collisions in OnCollisionEnter2D.
            // Force it to be a real ball so it bounces and hits pegs properly.
            if (activeBall.IsDummy)
            {
                activeBall.IsDummy = false;
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Cleared IsDummy flag on ball");
            }

            // Set aim direction AFTER InitializeMembers (which overwrites _aimVector)
            var aimField = HarmonyLib.AccessTools.Field(typeof(PachinkoBall), "_aimVector");
            var aimVec = new UnityEngine.Vector2(pending.AimDirectionX, pending.AimDirectionY).normalized;
            if (aimField != null)
            {
                aimField.SetValue(activeBall, aimVec);
                activeBallGO.transform.right = aimVec;
            }

            // Ensure ball is in AIMING state so Fire() works
            if (activeBall.CurrentState != PachinkoBall.FireballState.AIMING)
            {
                var stateProp = HarmonyLib.AccessTools.Property(typeof(PachinkoBall), "CurrentState");
                stateProp?.GetSetMethod(true)?.Invoke(activeBall, new object[] { PachinkoBall.FireballState.AIMING });
            }

            // Ensure ball is active BEFORE Fire() — something deactivates it between
            // DrawBall and our postfix. AddForce on an inactive object is silently ignored,
            // causing the ball to drop straight down with zero horizontal velocity.
            if (!activeBallGO.activeInHierarchy)
            {
                activeBallGO.SetActive(true);
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Pre-activated ball before Fire()");
            }

            // Save the client's target GUID so OnShotComplete can record it for
            // per-player damage resolution. Must happen before ConsumePendingShot.
            Events.Subscriptions.CoopSubscriptions.LastPendingShotTargetGuid = pending.TargetEnemyGuid;

            // Also set the host's TargetingManager to the client's target so
            // GetCurrentDamage and DoAttack use the right target context.
            try
            {
                if (!string.IsNullOrEmpty(pending.TargetEnemyGuid)
                    && services?.TryResolve<Utility.EnemyIdentifier>(out var eid) == true)
                {
                    var targetEnemy = eid.Find(pending.TargetEnemyGuid);
                    if (targetEnemy != null)
                    {
                        var targetMgr = UnityEngine.Object.FindObjectOfType<Battle.TargetingManager>();
                        targetMgr?.SetEnemyAsTarget(targetEnemy, force: true);
                    }
                }
            }
            catch
            {
            }

            // Use the real PachinkoBall.Fire() so all internal state (collision layers,
            // wall bounce tracking, shot timeout, etc.) is set up correctly.
            // ExecutingPendingShot bypasses PachinkoBall_Fire_Prefix's block.
            ExecutingPendingShot = true;
            try
            {
                activeBall.Fire();
                var rbAfter = activeBallGO.GetComponent<UnityEngine.Rigidbody2D>();
                var collider = activeBallGO.GetComponent<UnityEngine.CircleCollider2D>();
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[ClientPatches] PachinkoBall.Fire() succeeded, aim=({aimVec.x:F2},{aimVec.y:F2}), " +
                    $"pos=({activeBallGO.transform.position.x:F1},{activeBallGO.transform.position.y:F1}), " +
                    $"isDummy={activeBall.IsDummy}, scale=({activeBallGO.transform.localScale.x:F2}), " +
                    $"layer={LayerMask.LayerToName(activeBallGO.layer)}, " +
                    $"state={activeBall.CurrentState}, " +
                    $"rb.sim={rbAfter?.simulated}, rb.grav={rbAfter?.gravityScale:F1}, rb.mass={rbAfter?.mass:F2}, " +
                    $"collider={collider != null && collider.enabled}, radius={collider?.radius:F3}");
                // Start tracking ball position
                _firedBallGO = activeBallGO;
                _firedBallTimer = 0f;
                _firedBallLogCount = 0;
            }
            catch (System.Exception fireEx)
            {
                MultiplayerPlugin.Logger?.LogError($"[ClientPatches] Fire() failed even after InitializeMembers(): {fireEx}");
            }
            finally
            {
                ExecutingPendingShot = false;
            }

            // Always consume — even if something above failed partially
            Events.Handlers.Coop.ShootRequestClientHandler.ConsumePendingShot();

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatches] Executed PendingShot from {pending.PlayerName} (slot {pending.SlotIndex}): " +
                $"aim=({pending.AimDirectionX:F2},{pending.AimDirectionY:F2})");
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] PendingShot execution failed: {ex}");
        }
    }

    /// <summary>
    /// After DrawBall creates the active ball, ensure it's active on the host's turn.
    /// CoopStateManager.LoadDeckState stores deck objects with SetActive(false).
    /// Instantiate copies that inactive state, so the ball is inactive and can't
    /// be aimed or fired. This postfix activates it for the host's turn.
    ///
    /// During a client's turn the ball is DEACTIVATED so the host can't see or aim
    /// it. The pending-shot execution code reactivates it before calling Fire().
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "DrawBall")]
    [HarmonyPostfix]
    public static void BattleController_DrawBall_Postfix()
    {
        if (!UI.LobbyUI.GameStartReceived)
        {
            return;
        }

        var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
        if (bc == null)
        {
            return;
        }

        var activeBallField = HarmonyLib.AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
        var ballGO = activeBallField?.GetValue(bc) as UnityEngine.GameObject;
        if (ballGO == null)
        {
            return;
        }

        // On the host during a client's turn, hide the ball so the host
        // can't see the aimer or interact with it.
        if (IsHosting)
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<GameState.TurnManager>(out var tm) == true
                && tm.CurrentPlayerSlot > 0)
            {
                if (ballGO.activeInHierarchy)
                {
                    ballGO.SetActive(false);
                }

                MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] DrawBall postfix: hid ball '{ballGO.name}' (client's turn, slot {tm.CurrentPlayerSlot})");
                return;
            }
        }

        if (!ballGO.activeInHierarchy)
        {
            ballGO.SetActive(true);
            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] DrawBall postfix: activated ball '{ballGO.name}'");
        }
    }

    /// <summary>
    /// Block the host from discarding/skipping orbs during a client's turn
    /// (unless it's a programmatic discard from OrbDiscardRequest).
    /// Without this, the host's right-click discard would operate on the client's
    /// deck (loaded in singletons for their turn) instead of the host's.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "AttemptOrbDiscard")]
    [HarmonyPrefix]
    public static bool BattleController_AttemptOrbDiscard_Prefix()
    {
        if (!UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        // Block on client — discards are handled via OrbDiscardRequestEvent to host
        if (ShouldSuppressClientLogic)
        {
            return false;
        }

        if (!IsHosting)
        {
            return true;
        }

        if (_executingPendingDiscard)
        {
            return true; // bypass for programmatic discard
        }

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.TurnManager>(out var tm) == true
            && tm.CurrentPlayerSlot > 0) // client's turn
        {
            return false; // block manual discard from host player
        }

        return true;
    }

    // =========================================================================
    // CLIENT BATTLE INIT — fix assets + catch crashes in BattleController.Awake
    // =========================================================================

    /// <summary>
    /// Prefix: Destroy pre-instanced pegboard AND ensure MapDataBattle's
    /// pegboardFrame is non-null. The client finds the SO via Resources but
    /// the prefab references (pegboardFrame, background) may not be loaded
    /// because the game's normal asset preloading was skipped. Without a
    /// valid pegboardFrame, Awake crashes on Instantiate and kills the entire
    /// init chain (LoadEnemyAssets, EnemyManager.Initialize, pegboard loading).
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "Awake")]
    [HarmonyPrefix]
    public static void BattleController_Awake_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return;
        }

        // 1. Destroy pre-instanced pegs
        var preData = StaticGameData.preInstancedPegboardData;
        if (preData != null)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Destroying preInstancedPegboardData on client " +
                $"(pegboard={preData.pegboardData?.name}, root={preData.rootGameObject?.name})");
            if (preData.rootGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(preData.rootGameObject);
            }

            StaticGameData.preInstancedPegboardData = null;
        }

        // 2. Ensure pegboardFrame is not null — create a dummy if needed.
        //    The actual pegs come from TryLoadPegLayout, not from the frame.
        //    The frame is just the visual border which is cosmetic.
        var battle = StaticGameData.dataToLoad as Data.MapDataBattle;
        if (battle != null)
        {
            if (battle.pegboardFrame == null)
            {
                MultiplayerPlugin.Logger?.LogWarning("[ClientPatches] pegboardFrame is null — creating dummy to prevent Awake crash");
                battle.pegboardFrame = new GameObject("ClientDummyPegboardFrame");
            }

            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Battle init: name={battle.name}, " +
                $"pegLayout={battle.pegLayout?.name}, pegboardFrame={battle.pegboardFrame?.name}, " +
                $"starterSpawns={battle.starterSpawns?.Count ?? -1}, waves={battle.waveGroups?.Length ?? -1}, " +
                $"slots={battle.NumberOfSlots}, background={battle.background?.name ?? "NULL"}");
        }
        else
        {
            MultiplayerPlugin.Logger?.LogWarning("[ClientPatches] dataToLoad is not MapDataBattle — BattleController.Awake may fail");
        }

        // 3. Restore host's RNG state so RandomPegField generates identical positions.
        //    This was captured at node activation and sent via NodeActivatedEvent.
        if (!string.IsNullOrEmpty(PendingBattleRngState))
        {
            var restored = DeserializeRandomState(PendingBattleRngState);
            if (restored.HasValue)
            {
                Random.state = restored.Value;
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Restored host RNG state for pegboard generation");
            }

            PendingBattleRngState = null;
        }
    }

    /// <summary>
    /// Finalizer: Catch ANY exception from BattleController.Awake on client.
    /// Logs the full stack trace and swallows the exception so the game continues.
    /// After a crash, our sync system will still apply state from the host.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "Awake")]
    [HarmonyFinalizer]
    public static Exception BattleController_Awake_Finalizer(Exception __exception)
    {
        if (__exception == null)
        {
            return null;
        }

        if (!ShouldSuppressClientLogic)
        {
            return __exception;
        }

        MultiplayerPlugin.Logger?.LogError($"[ClientPatches] BattleController.Awake CRASHED on client (swallowed):\n" +
            $"  {__exception.GetType().Name}: {__exception.Message}\n{__exception.StackTrace}");

        // Try to do minimal recovery — load enemy prefabs and set BattleActive
        try
        {
            BattleController.BattleActive = true;

            var battle = StaticGameData.dataToLoad as Data.MapDataBattle;
            if (battle?.starterSpawns != null)
            {
                var cache = Loading.AssetLoading.Instance?.EnemyPrefabs;
                if (cache != null)
                {
                    var loaded = 0;
                    foreach (var spawn in battle.starterSpawns)
                    {
                        try
                        {
                            if (spawn?.spawnData?.enemyAssetReference == null)
                            {
                                continue;
                            }

                            var key = spawn.spawnData.enemyAssetReference.RuntimeKey.ToString();
                            if (!cache.ContainsKey(key))
                            {
                                var go = spawn.spawnData.enemyAssetReference.LoadAssetAsync<GameObject>().WaitForCompletion();
                                if (go != null)
                                {
                                    cache[key] = go;
                                    loaded++;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }

                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Recovery: loaded {loaded} enemy prefabs (cache={cache.Count})");
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[ClientPatches] Recovery failed: {ex.Message}");
        }

        return null; // Swallow — sync system will handle state
    }

    /// <summary>Block board field reset on client — prevents re-shuffling pegs.
    /// In coop, allow during client's turn so board refreshes work.</summary>
    [HarmonyPatch(typeof(BattleController), "ResetField")]
    [HarmonyPrefix]
    public static bool BattleController_ResetField_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }
        // In coop, allow field reset during client's turn
        if (UI.LobbyUI.GameStartReceived && Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Block gold coin placement on client pegs. BattleController.Start() calls
    /// AddInitialCoinsToBoard which shuffles _allPegs randomly and places gold.
    /// Our PegApplier syncs gold state per-peg from the host.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "AddInitialCoinsToBoard")]
    [HarmonyPrefix]
    public static bool BattleController_AddInitialCoinsToBoard_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked AddInitialCoinsToBoard — host will send gold state");
        return false;
    }

    // =========================================================================
    // LIVE PENDING DAMAGE OVERLAY — update per peg hit during coop
    // =========================================================================

    /// <summary>
    /// Block BattleController.HandlePegActivated on the client. The host runs all
    /// peg-driven game logic (crit activation, damage queueing, peg multihits, relic
    /// checks) and forwards the resulting visual state via DamageTextEvent / heartbeat.
    /// If Peg.OnPegActivated ever fires on the client (e.g. from a stray PegActivated()
    /// call in our own appliers), we must not let the original handler run — it
    /// would call QueueCritTextDisplay / QueueDamageTextDisplay and render a SECOND
    /// "Crit!" or damage number on top of the one we already received from the host.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "HandlePegActivated")]
    [HarmonyPrefix]
    public static bool BattleController_HandlePegActivated_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// After each peg activation, compute the running damage total for the
    /// current player and dispatch a PendingDamagePreviewEvent so both host
    /// and client render persistent damage text above targeted enemies.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "HandlePegActivated")]
    [HarmonyPostfix]
    public static void BattleController_HandlePegActivated_Postfix(BattleController __instance)
    {
        if (!IsHosting)
        {
            return;
        }

        if (!UI.LobbyUI.GameStartReceived)
        {
            return;
        }

        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                return;
            }

            if (!services.TryResolve<GameState.CoopStateManager>(out var coopState))
            {
                return;
            }

            if (coopState.TotalPlayerCount < 2)
            {
                return;
            }

            if (!services.TryResolve<IGameEventRegistry>(out var registry))
            {
                return;
            }

            var activeSlot = coopState.ActivePlayerSlot;

            // Read BattleController's running tallies
            var pegTallyField = AccessTools.Field(typeof(BattleController), "_pegMultiplierDamageTally");
            var critField = AccessTools.Field(typeof(BattleController), "_criticalHitCount");
            var dmgMultField = AccessTools.Field(typeof(BattleController), "_damageMultiplier");
            var dmgBonusField = AccessTools.Field(typeof(BattleController), "_damageBonus");

            var pegTally = pegTallyField != null ? (int)pegTallyField.GetValue(__instance) : 0;
            var critCount = critField != null ? (int)critField.GetValue(null) : 0; // static
            var dmgMult = dmgMultField != null ? (float)dmgMultField.GetValue(__instance) : 1f;
            long dmgBonus = dmgBonusField != null ? (int)dmgBonusField.GetValue(__instance) : 0;

            // Compute running damage via AttackManager
            var amField = AccessTools.Field(typeof(BattleController), "_attackManager");
            var am = amField?.GetValue(__instance) as Battle.Attacks.AttackManager;
            if (am == null)
            {
                return;
            }

            var currentDamage = am.GetCurrentDamage(pegTally, dmgMult, dmgBonus, critCount);

            // Track the high-water mark across the entire shot so OnShotComplete
            // can fall back to this if BC's _pegMultiplierDamageTally has been
            // zeroed before we capture (defensive — see CoopSubscriptions for
            // the full rationale around multiball satellite timing).
            if (pegTally > Events.Subscriptions.CoopSubscriptions.HighWaterPegTally)
            {
                Events.Subscriptions.CoopSubscriptions.HighWaterPegTally = pegTally;
            }

            if (currentDamage > Events.Subscriptions.CoopSubscriptions.HighWaterDamage)
            {
                Events.Subscriptions.CoopSubscriptions.HighWaterDamage = currentDamage;
            }

            if (currentDamage <= 0 && am.isHeal)
            {
                return; // heal orbs — no damage preview
            }

            // Get target and AoE status
            var isAoE = false;
            var attackField = AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_attack");
            var attack = attackField?.GetValue(am) as Battle.Attacks.Attack;
            if (attack is Battle.Attacks.SimpleAttack)
            {
                isAoE = true;
            }

            string targetGuid = null;
            var tmgr = UnityEngine.Object.FindObjectOfType<Battle.TargetingManager>();
            if (tmgr?.currentTarget != null && services.TryResolve<Utility.EnemyIdentifier>(out var eid))
            {
                targetGuid = eid.GetGuid(tmgr.currentTarget);
            }

            // Player name
            var playerName = $"Slot {activeSlot}";
            if (coopState.PlayerStates.TryGetValue(activeSlot, out var pState))
            {
                playerName = pState.PlayerName ?? playerName;
            }

            // Build event: previous players' finalized data + current player's live total
            var entries = CoopSubscriptions.GetAccumulatedDamageEntries()
                ?? new System.Collections.Generic.List<Events.Network.Coop.PendingDamagePreviewEvent.DamageEntry>();

            // Replace or add current player's live entry
            var replaced = false;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].SlotIndex == activeSlot)
                {
                    entries[i] = new Events.Network.Coop.PendingDamagePreviewEvent.DamageEntry
                    {
                        SlotIndex = activeSlot,
                        PlayerName = playerName,
                        Damage = currentDamage,
                        TargetEnemyGuid = targetGuid,
                        IsAoE = isAoE,
                    };
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                entries.Add(new Events.Network.Coop.PendingDamagePreviewEvent.DamageEntry
                {
                    SlotIndex = activeSlot,
                    PlayerName = playerName,
                    Damage = currentDamage,
                    TargetEnemyGuid = targetGuid,
                    IsAoE = isAoE,
                });
            }

            registry.Dispatch(new Events.Network.Coop.PendingDamagePreviewEvent { Entries = entries });

            // Dispatch only runs ServerHandler (sends to clients over network).
            // Apply locally on host so the overlay renders here too.
            foreach (var entry in entries)
            {
                UI.PendingDamageOverlay.SetPlayerDamage(
                    entry.SlotIndex,
                    entry.PlayerName,
                    entry.Damage,
                    entry.TargetEnemyGuid,
                    entry.IsAoE);
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopDmgOverlay] HandlePegActivated postfix failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reset per-shot multiball diagnostic counters when a new shot starts.
    /// Pairs with HandlePegActivated_Postfix and OnShotComplete (CoopSubscriptions)
    /// to provide a defensive damage floor for shots whose pegTally gets zeroed
    /// before our capture (multiball satellite destruction races).
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "ShotFired")]
    [HarmonyPostfix]
    public static void BattleController_ShotFired_Postfix()
    {
        if (!IsHosting)
        {
            return;
        }

        Events.Subscriptions.CoopSubscriptions.HighWaterPegTally = 0;
        Events.Subscriptions.CoopSubscriptions.HighWaterDamage = 0;
        Events.Subscriptions.CoopSubscriptions.MultiballSpawnCount = 0;

        // Reset stuck-completion watchdog timestamps for the new shot.
        _shotFiredUnscaledTime = UnityEngine.Time.unscaledTime;
        _stuckCompletionSinceUnscaledTime = 0f;
        _stuckCompletionScanTimer = 0f;
    }

    /// <summary>
    /// Count multiball satellite spawns so OnShotComplete can log them when
    /// diagnosing damage-capture issues for multiball orbs.
    /// </summary>
    [HarmonyPatch(typeof(PachinkoBall), "SpawnMultiball")]
    [HarmonyPostfix]
    public static void PachinkoBall_SpawnMultiball_Postfix()
    {
        if (!IsHosting)
        {
            return;
        }

        Events.Subscriptions.CoopSubscriptions.MultiballSpawnCount++;
    }

    // =========================================================================
    // PER-PLAYER DAMAGE RESOLUTION — apply non-host damage in coop DoAttack
    // =========================================================================

    /// <summary>
    /// Before the host's normal DoAttack runs, apply each non-host player's
    /// pre-computed damage to their individually targeted enemy. The host's
    /// attack proceeds normally via the original DoAttack method.
    ///
    /// This prefix does NOT skip the original — it applies extra damage first,
    /// then lets the host's attack run as normal (return true).
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "DoAttack")]
    [HarmonyPrefix]
    public static bool BattleController_DoAttack_Prefix(BattleController __instance)
    {
        if (!IsHosting)
        {
            return true;
        }

        if (!UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        // Clear the pending damage overlay — the attack is now resolving
        try
        {
            UI.PendingDamageOverlay.ClearAll();
            if (MultiplayerPlugin.Services?.TryResolve<IGameEventRegistry>(out var clearReg) == true)
            {
                clearReg.Dispatch(new Events.Network.Coop.PendingDamagePreviewEvent());
            }
        }
        catch
        {
        }

        try
        {
            // Apply ALL players' damage through a sequential visual pipeline.
            // For each slot that shot this round, we play the peglin throw animation,
            // fly a visual-only projectile carrying that player's orb sprite toward
            // the target, then apply damage on impact. Clients receive one
            // AttackStartedEvent per shot and mirror the visual via ClientAttackProjectile.
            var allShots = Events.Subscriptions.CoopSubscriptions.ConsumeNonHostShotData();
            if (allShots == null || allShots.Count == 0)
            {
                return true;
            }

            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<Utility.EnemyIdentifier>(out var enemyId) != true)
            {
                return true;
            }

            var em = UnityEngine.Object.FindObjectOfType<EnemyManager>();
            if (em == null)
            {
                return true;
            }

            // Hold the ATTACKING state while the sequence plays. AttackManager.IsAttacking()
            // is polled by BattleController.Update; without this the state machine would
            // advance the moment DoAttack returns.
            var amField = AccessTools.Field(typeof(BattleController), "_attackManager");
            var am = amField?.GetValue(__instance) as Battle.Attacks.AttackManager;
            var isAttackingField = AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_isAttacking");
            var animFinishedField = AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_attackAnimationFinished");
            if (am != null)
            {
                isAttackingField?.SetValue(am, true);
                animFinishedField?.SetValue(am, false);
            }

            // Suppress the delegate-driven dispatch that StartAttacking() will fire
            // right after we return — the coroutine emits its own per-slot events.
            SuppressOnAttackStartedDispatch = true;

            __instance.StartCoroutine(PlayCoopAttackSequence(__instance, am, allShots, em, enemyId));

            // Skip the original DoAttack — the coroutine above runs the full damage +
            // visuals pipeline and releases the AttackManager state when it finishes.
            return false;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopAttack] Per-player damage resolution failed: {ex}");
            return true; // Fall back to original DoAttack on error
        }
    }

    // =========================================================================
    // BATTLE INIT — re-subscribe CoopSubscriptions at the start of every battle
    // =========================================================================

    /// <summary>
    /// Re-subscribe CoopSubscriptions to BattleController static delegates at
    /// the start of every battle. This ensures the turn system handlers are
    /// active regardless of any timing issues with the initial subscription
    /// (e.g., delegate references being replaced across scene loads).
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "Awake")]
    [HarmonyPostfix]
    public static void BattleController_Awake_Postfix()
    {
        if (!IsHosting)
        {
            return;
        }

        if (!UI.LobbyUI.GameStartReceived)
        {
            return;
        }

        var coopSubs = CoopSubscriptions.Instance;
        if (coopSubs == null)
        {
            MultiplayerPlugin.Logger?.LogWarning("[ClientPatches] BattleController.Awake: CoopSubscriptions.Instance is null");
            return;
        }

        coopSubs.Subscribe();
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] BattleController.Awake: re-subscribed CoopSubscriptions");

        // Merge all players' board-affecting relics into _ownedRelics BEFORE
        // BattleController.Start() runs. Start() checks relics for bombs, coins,
        // crit/refresh peg counts, etc. Without the merge, only the host's relics
        // affect the board even if a client has ADDITIONAL_STARTING_BOMBS, etc.
        // The relics are restored to the active player after OnBattleStarted.
        if (MultiplayerPlugin.Services?.TryResolve<GameState.CoopStateManager>(out var coopState) == true)
        {
            coopState.MergeBoardRelics();
        }
    }

    /// <summary>
    /// Coop two-outcome resolution for TriggerVictory.
    /// The native game bails silently when the host's HP is 0, which stalls the
    /// battle state machine in ATTACKING forever if the host died but other players
    /// killed the enemies. Per the coop rules, there are only two outcomes once the
    /// round resolves:
    ///   (A) all players dead → fire the defeat flow (skip native victory).
    ///   (B) at least one alive → revive the host to 1 HP so native victory proceeds;
    ///       CoopSubscriptions.OnVictory then revives every dead player to 1 HP and
    ///       broadcasts the updated state to clients via the heartbeat / SyncAll.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "TriggerVictory")]
    [HarmonyPrefix]
    public static bool BattleController_TriggerVictory_Prefix()
    {
        if (!IsHosting)
        {
            return true;
        }

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) != true)
        {
            return true;
        }

        if (coopState.TotalPlayerCount < 2)
        {
            return true;
        }

        var phc = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
        if (phc == null)
        {
            return true;
        }

        // Case A: everyone is dead — drive the defeat flow and skip native victory.
        // The PHC.CheckForDeathAndUpdateBar prefix lets it through when AllPlayersDead.
        if (coopState.AllPlayersDead)
        {
            try
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    "[ClientPatches] TriggerVictory: all players dead — routing to defeat instead of victory");
                phc.CheckForDeathAndUpdateBar();
            }
            catch (System.Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[ClientPatches] TriggerVictory defeat route failed: {ex.Message}");
            }

            return false;
        }

        // Case B: at least one player alive. If the host is dead, revive native PHC
        // to 1 HP so the native victory check (hp > 0) passes and OnVictory fires.
        if (phc.CurrentHealth <= 0f)
        {
            try
            {
                var healthField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_playerHealth");
                var healthVar = healthField?.GetValue(phc);
                if (healthVar != null)
                {
                    var setMethod = healthVar.GetType().GetMethod("Set", new[] { typeof(float) });
                    setMethod?.Invoke(healthVar, new object[] { 1f });
                }

                var hostState = coopState.GetPlayerState(0);
                hostState?.CurrentHealth = 1;

                MultiplayerPlugin.Logger?.LogInfo(
                    "[ClientPatches] TriggerVictory: host was dead but others alive — revived host PHC to 1 HP so victory fires");
            }
            catch (System.Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[ClientPatches] TriggerVictory host revive failed: {ex.Message}");
            }
        }

        return true;
    }

    // =========================================================================
    // COOP ROUND COUNT — only increment _roundCount once per full round
    // =========================================================================

    /// <summary>
    /// Snapshot used by the round-count fix to suppress per-turn increments.
    /// </summary>
    public struct RoundCountSnapshot
    {
        public bool Engaged;
        public int SavedRoundCount;
        public BattleController.RoundCountIncremented SavedDelegate;
    }

    // Tracks the TurnManager round number for which we last actually fired
    // OnRoundCountIncremented. Resets when the turn manager's round counter
    // resets (new battle).
    private static int _lastFiredCoopRound;

    /// <summary>
    /// In coop the BattleController re-enters AWAITING_SHOT for every player's
    /// turn (and at the start of the next real round). Each entry runs
    /// `_roundCount++; OnRoundCountIncremented?.Invoke()`. That breaks any
    /// consumer that treats RoundCount as battle rounds — Spirit of Radia's
    /// countdown text in particular ticks down N× per real round and phase 2
    /// transitions early.
    ///
    /// We can't tell from inside Update which entry corresponds to a "real new
    /// round" without external context, so:
    ///   1. Prefix: snapshot _roundCount + null the delegate so Update's inline
    ///      `Invoke` is a no-op (we'll manually invoke later when appropriate).
    ///   2. Postfix: restore the delegate. If Update incremented _roundCount,
    ///      consult TurnManager.RoundNumber. If it advanced past
    ///      `_lastFiredCoopRound`, this is the first player turn of a real new
    ///      round → invoke the delegate exactly once with the new count and
    ///      bump the tracker. Otherwise this was a mid-round player swap →
    ///      roll back _roundCount to its pre-Update value.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "Update")]
    [HarmonyPrefix]
    public static void BattleController_Update_RoundCount_Prefix(out RoundCountSnapshot __state)
    {
        __state = default;
        if (!IsHosting || !UI.LobbyUI.GameStartReceived)
        {
            return;
        }

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coop) != true
            || coop.TotalPlayerCount < 2)
        {
            return;
        }

        __state.Engaged = true;
        __state.SavedRoundCount = BattleController.RoundCount;
        __state.SavedDelegate = BattleController.OnRoundCountIncremented;
        BattleController.OnRoundCountIncremented = null;
    }

    [HarmonyPatch(typeof(BattleController), "Update")]
    [HarmonyPostfix]
    public static void BattleController_Update_RoundCount_Postfix(RoundCountSnapshot __state)
    {
        if (!__state.Engaged)
        {
            return;
        }

        BattleController.OnRoundCountIncremented = __state.SavedDelegate;

        var newCount = BattleController.RoundCount;
        if (newCount == __state.SavedRoundCount)
        {
            return; // Update didn't enter the AWAITING_SHOT-entry branch
        }

        var services = MultiplayerPlugin.Services;
        GameState.TurnManager tm = null;
        services?.TryResolve<GameState.TurnManager>(out tm);

        // Reset the tracker when the TurnManager has reset (new battle).
        if (tm != null && tm.RoundNumber < _lastFiredCoopRound)
        {
            _lastFiredCoopRound = 0;
        }

        var coopRound = tm?.RoundNumber ?? 0;
        if (coopRound > _lastFiredCoopRound)
        {
            // First sub-turn of a real new round — fire exactly once.
            _lastFiredCoopRound = coopRound;
            try
            {
                __state.SavedDelegate?.Invoke(newCount);
            }
            catch (System.Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopRoundCount] Delegate invoke failed: {ex.Message}");
            }
        }
        else
        {
            // Mid-round player swap re-entered AWAITING_SHOT — undo the
            // bogus increment so consumers see the same RoundCount until
            // a real new round starts.
            var f = AccessTools.Field(typeof(BattleController), "_roundCount");
            f?.SetValue(null, __state.SavedRoundCount);
        }
    }

    /// <summary>
    /// Suppress the client's own ThrowAllBombs coroutine. Without this, the
    /// client throws bombs using its local RNG (wrong positions), damages
    /// enemies independently, and creates duplicate visuals alongside the
    /// host's BombThrownEvent visual-only bombs. We zero the bomb counters
    /// so the BattleController state machine transitions past THROW_BOMBS.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "ThrowAllBombs")]
    [HarmonyPrefix]
    public static bool BattleController_ThrowAllBombs_Prefix(
        BattleController __instance, ref IEnumerator __result)
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        _bombsRegularField?.SetValue(__instance, 0);
        _bombsRiggedField?.SetValue(__instance, 0);

        __result = EmptyCoroutine();
        return false;
    }
}
