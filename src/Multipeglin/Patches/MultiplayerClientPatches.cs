using System;
using System.Collections;
using System.Linq;
using Battle;
using HarmonyLib;
using Loading;
using Multipeglin.Events;
using Multipeglin.Multiplayer;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Multipeglin.Patches;

[HarmonyPatch]
public static class MultiplayerClientPatches
{
    /// <summary>
    /// UnityEngine.Random.State captured BEFORE MapController generates the map.
    /// MapStateProvider reads this to include in the snapshot sent to clients.
    /// </summary>
    internal static string CapturedPreMapGenRngState;

    /// <summary>
    /// RNG state received from the host, to be restored before client map generation.
    /// </summary>
    internal static string PendingRngStateToRestore;

    /// <summary>
    /// RNG state received from host at node activation, restored before pegboard
    /// generation so RandomPegField produces identical positions on client.
    /// </summary>
    internal static string PendingBattleRngState;

    /// <summary>
    /// Set to true when MapController.Start completes on the client.
    /// The pending snapshot coroutine waits for this before applying node types.
    /// </summary>
    internal static bool MapControllerStartCompleted;

    /// <summary>
    /// Set to true by our sync handlers right before they call LoadScene.
    /// The PeglinSceneLoader patch checks this flag and blocks all other scene loads.
    /// Reset to false after the load is initiated.
    /// </summary>
    internal static bool AllowNextSceneLoad;

    /// <summary>
    /// Returns true when the client should NOT run its own game logic.
    /// Only true when actively connected as a spectating client.
    /// </summary>
    internal static bool ShouldSuppressClientLogic
    {
        get
        {
            if (MultiplayerPlugin.Services == null)
            {
                return false;
            }

            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode))
            {
                return false;
            }

            return mode.IsSpectating;
        }
    }

    internal static bool IsHosting
    {
        get
        {
            if (MultiplayerPlugin.Services == null)
            {
                return false;
            }

            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode))
            {
                return false;
            }

            return mode.IsHosting;
        }
    }

    /// <summary>
    /// Set to true by BattleController_Update_Postfix just before calling
    /// PachinkoBall.Fire() to execute a client's pending shot. This prevents
    /// PachinkoBall_Fire_Prefix from blocking the programmatic fire.
    /// </summary>
    internal static bool ExecutingPendingShot;

    /// <summary>
    /// Set to true while the client's native BattleUpgradeCanvas is open.
    /// Allows blocked methods (AddGold, RemoveGold, AddOrbToDeck, Damage, AddRelic)
    /// to execute so the reward screen works normally.
    /// </summary>
    internal static bool AllowNativeRewardLogic;

    /// <summary>
    /// Set to true while the client is in the ShopScenario scene and allowed to purchase.
    /// </summary>
    internal static bool AllowShopLogic;

    /// <summary>
    /// Set to true while the client is in the Treasure scene and allowed to interact with chest/relic UI.
    /// </summary>
    internal static bool AllowTreasureLogic;

    /// <summary>
    /// Set to true while the client is in the PegMinigame scene and allowed to play independently.
    /// </summary>
    internal static bool AllowPegMinigameLogic;

    /// <summary>
    /// Set to true while the client is in the TextScenario scene and allowed to
    /// interact with native dialogue (mirror, altar, etc.).
    /// </summary>
    internal static bool AllowTextScenarioLogic;

    /// <summary>
    /// Set to true on the client during the parallel-shoot end-of-stage navigate
    /// phase. Allows the nav ball to arm, aim, and fire locally; HandleSlotTriggerActivated
    /// patches divert the slot hit into a NavigateVoteEvent instead of triggering victory.
    /// </summary>
    internal static bool AllowNavigateLogic;

    /// <summary>
    /// Tracks the relic effect chosen by the client during the post-battle
    /// boss/rare relic selection. Reset when the reward phase ends.
    /// -1 means no relic chosen (skipped or not yet selected).
    /// </summary>
    internal static int ClientChosenPostBattleRelicEffect = -1;
    internal static string ClientChosenPostBattleRelicName;

    // Track fired ball for position diagnostics
    internal static UnityEngine.GameObject _firedBallGO;
    internal static float _firedBallTimer;
    internal static int _firedBallLogCount;

    /// <summary>
    /// The primary ball currently being tracked (the one whose position is streamed
    /// as BallPositionEvent). HostBallRegistry uses this to avoid attaching a
    /// duplicate streamer to the primary ball (which would render twice on the client).
    /// </summary>
    public static UnityEngine.GameObject PrimaryBall => _firedBallGO;

    /// <summary>
    /// Mirror the class choice into RelicManager by calling PopulateRelicPools.
    /// The native class-select UI does this via LoadoutManager.SetupLoadout, but
    /// we skip that flow in multiplayer. Without this, RelicManager._selectedClass
    /// stays at the default (Peglin), and the relic queue stays populated with
    /// Peglin-class relics — so the shop/treasure on the client shows either zero
    /// relics or wrong-class relics.
    /// </summary>
    public static void SetRelicManagerClass(Peglin.ClassSystem.Class chosenClass)
    {
        try
        {
            var rms = Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
            if (rms == null || rms.Length == 0)
            {
                MultiplayerPlugin.Logger?.LogWarning("[ClientPatches] RelicManager not found — shop relics may be wrong class");
                return;
            }

            var rm = rms[0];
            rm.PopulateRelicPools(chosenClass);
            // SetupInternalRelicPools fills _availableCommonRelics from CommonRelicPool
            // and seeds the random-queue order. Without it, GetMultipleRelicsOfRarity /
            // GetMultipleRelicsOffOfQueue on the client return only Consolation Prize
            // (e.g. PegMinigame "?" room with no bouncers) since the available list is
            // empty until something else triggers it. The host hits this path naturally
            // via GameInit; on the client we have to do it explicitly.
            try
            {
                rm.SetupInternalRelicPools();
            }
            catch (Exception sx) { MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] SetupInternalRelicPools failed: {sx.Message}"); }

            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Called RelicManager.PopulateRelicPools({chosenClass}) + SetupInternalRelicPools");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] SetRelicManagerClass failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Mirror the class choice into CruciballManager.currentClass. The native
    /// class-select UI normally does this via SetRunCruciballLevelAndClass, but
    /// we skip that flow in multiplayer (lobby already picked the class). Without
    /// this, PeglinClassAnimationSwitcher.OnEnable in the Battle scene sees the
    /// default class and renders the local player as plain Peglin regardless of
    /// the lobby choice.
    /// </summary>
    public static void SetCruciballManagerClass(Peglin.ClassSystem.Class chosenClass)
    {
        try
        {
            var cms = Resources.FindObjectsOfTypeAll<Cruciball.CruciballManager>();
            if (cms == null || cms.Length == 0)
            {
                MultiplayerPlugin.Logger?.LogWarning("[ClientPatches] CruciballManager not found — class sprite may be wrong");
                return;
            }

            var cm = cms[0];
            cm.currentClass = chosenClass;
            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Set CruciballManager.currentClass = {chosenClass}");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] SetCruciballManagerClass failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Set CruciballManager.currentCruciballLevel directly. Used by both host and
    /// client at game-start to apply the level chosen in the multiplayer lobby — the
    /// native CruciballLevelSelector UI is skipped in multiplayer.
    /// </summary>
    public static void SetCruciballManagerLevel(int level)
    {
        try
        {
            var cms = Resources.FindObjectsOfTypeAll<Cruciball.CruciballManager>();
            if (cms == null || cms.Length == 0)
            {
                MultiplayerPlugin.Logger?.LogWarning("[ClientPatches] CruciballManager not found — cruciball level not applied");
                return;
            }

            var cm = cms[0];
            cm.currentCruciballLevel = level;
            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Set CruciballManager.currentCruciballLevel = {level}");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] SetCruciballManagerLevel failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Look up the ClassLoadoutData for the given class and set StaticGameData.StartingOrbs
    /// and StaticGameData.StartingRelics so GameInit.Start() can initialize the deck properly.
    /// </summary>
    public static void SetStartingLoadoutFromClass(Peglin.ClassSystem.Class chosenClass)
    {
        var classLoadouts = StaticGameData.classLoadouts;
        if (classLoadouts == null || classLoadouts.Length == 0)
        {
            MultiplayerPlugin.Logger?.LogWarning("[ClientPatches] StaticGameData.classLoadouts is null/empty — cannot set starting orbs");
            return;
        }

        Peglin.ClassSystem.ClassLoadoutData loadout = null;
        foreach (var pair in classLoadouts)
        {
            if (pair.Class == chosenClass)
            {
                loadout = pair.Loadout;
                break;
            }
        }

        if (loadout == null)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] No ClassLoadoutData found for class {chosenClass}");
            return;
        }

        StaticGameData.StartingOrbs = loadout.StartingOrbs?.ToArray();
        StaticGameData.StartingRelics = loadout.StartingRelics?.ToArray();

        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Set starting loadout for {chosenClass}: " +
            $"{StaticGameData.StartingOrbs?.Length ?? 0} orbs, {StaticGameData.StartingRelics?.Length ?? 0} relics");
    }

    /// <summary>
    /// Tracks whether the client has already sent a ShootRequest this turn.
    /// Reset when TurnChangeClientHandler receives a new turn change.
    /// </summary>
    internal static bool ClientShotSentThisTurn;

    /// <summary>
    /// Tracks whether the client has sent an OrbDiscardRequest this turn.
    /// Reset when OrbDiscardedEvent comes back (confirming the discard) or on turn change.
    /// Prevents sending multiple discard requests before the host responds.
    /// </summary>
    internal static bool _clientDiscardSentThisTurn;

    // Throttled aim update sending from client to host (10 Hz)
    internal static float _clientAimSendTimer;
    internal const float ClientAimSendInterval = 0.1f;

    // Track whether we've initialized the ball for client aiming this turn
    internal static bool _clientBallInitialized;

    // The client-created ball GO for the orb visual at spawn point
    internal static UnityEngine.GameObject _clientBallGO;

    // Our own trajectory LineRenderer (separate from the ball's TrajectorySimulation)
    internal static UnityEngine.GameObject _clientTrajectoryGO;
    internal static UnityEngine.LineRenderer _clientTrajectoryLR;

    // Physics parameters read from the ball prefab for trajectory calculation
    internal static float _clientFireForce;
    internal static float _clientBallMass;
    internal static float _clientGravityScale;

    /// <summary>
    /// Reset the client aiming ball so it gets re-created on the next frame.
    /// Called when the host confirms an orb discard — the new orb is in the
    /// shuffled deck and HandleClientAiming will pick it up.
    /// </summary>
    internal static void ResetClientAimingBall()
    {
        _clientDiscardSentThisTurn = false;
        CleanupClientAiming();
    }

    /// <summary>
    /// Clean up client aiming objects (ball visual + trajectory line).
    /// Called when the turn ends or aiming is no longer active.
    /// </summary>
    internal static void CleanupClientAiming()
    {
        // Clean up prediction visuals and clear _activePachinkoBall
        if (_clientBallGO != null)
        {
            try
            {
                // Tell PredictionManager to clean up trajectory dots/lines
                var ball = _clientBallGO.GetComponent<PachinkoBall>();
                if (ball != null)
                {
                    var pmField = HarmonyLib.AccessTools.Field(typeof(PachinkoBall), "_predictionManager");
                    var pm = pmField?.GetValue(ball) as PredictionManager;
                    try
                    {
                        pm?.PlayerFired();
                    }
                    catch
                    {
                    }
                }

                var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
                if (bc != null)
                {
                    var field = HarmonyLib.AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
                    if ((object)field?.GetValue(bc) == _clientBallGO)
                    {
                        field.SetValue(bc, null);
                    }
                }
            }
            catch
            {
            }

            UnityEngine.Object.Destroy(_clientBallGO);
            _clientBallGO = null;
        }

        if (_clientTrajectoryGO != null)
        {
            UnityEngine.Object.Destroy(_clientTrajectoryGO);
            _clientTrajectoryGO = null;
        }

        _clientTrajectoryLR = null;
        _clientBallInitialized = false;
        _clientBallRetryCount = 0;
        _clientDiscardSentThisTurn = false;
    }

    /// <summary>
    /// Create a ball visual at the spawn point and a custom trajectory LineRenderer.
    /// The game's PredictionManager (which normally renders trajectories) requires too
    /// many dependencies to work on the client. Instead we build our own LineRenderer
    /// using the same physics formula as TrajectorySimulation.simulatePath().
    /// </summary>
    // Retry counter to avoid infinite retry spam if ball creation keeps failing
    internal static int _clientBallRetryCount;
    internal const int MaxBallRetries = 30; // ~0.5s at 60fps

    internal static void HandleClientAiming(BattleController bc)
    {
        if (!_clientBallInitialized)
        {
            // Rate-limit retries to avoid log spam
            if (_clientBallRetryCount >= MaxBallRetries)
            {
                return;
            }

            CleanupClientAiming();

            // Get spawn position
            var spawnPos = bc.pachinkoBallSpawnLocation;
            if (spawnPos == UnityEngine.Vector2.zero)
            {
                var player = UnityEngine.GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    spawnPos = (UnityEngine.Vector2)player.transform.position;
                }
            }

            try
            {
                // Get the orb prefab from the deck
                var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
                var dm = dms.Length > 0 ? dms[0] : null;
                if (dm == null)
                {
                    _clientBallRetryCount++;
                    if (_clientBallRetryCount == 1 || _clientBallRetryCount == MaxBallRetries)
                    {
                        MultiplayerPlugin.Logger?.LogWarning($"[ClientAim] No DeckManager (retry {_clientBallRetryCount})");
                    }

                    return;
                }

                var shuffledField = HarmonyLib.AccessTools.Field(typeof(DeckManager), "shuffledDeck");
                var shuffled = shuffledField?.GetValue(dm) as System.Collections.Generic.Stack<UnityEngine.GameObject>;
                UnityEngine.GameObject prefab = null;
                if (shuffled != null && shuffled.Count > 0)
                {
                    prefab = shuffled.Peek();
                }

                if (prefab == null && DeckManager.completeDeck != null && DeckManager.completeDeck.Count > 0)
                {
                    prefab = DeckManager.completeDeck[0];
                }

                if (prefab == null)
                {
                    _clientBallRetryCount++;
                    if (_clientBallRetryCount == 1 || _clientBallRetryCount == MaxBallRetries)
                    {
                        MultiplayerPlugin.Logger?.LogWarning($"[ClientAim] No orb prefab (shuffled={shuffled?.Count ?? -1}, complete={DeckManager.completeDeck?.Count ?? -1}, retry {_clientBallRetryCount})");
                    }

                    return;
                }

                // Instantiate ball for the orb visual at spawn point
                _clientBallGO = UnityEngine.Object.Instantiate(prefab, spawnPos, UnityEngine.Quaternion.identity);
                _clientBallGO.SetActive(true); // Deck orb prefabs may be inactive — force active so Update() runs

                // Read physics parameters for trajectory calculation
                var ball = _clientBallGO.GetComponent<PachinkoBall>();
                var rb = _clientBallGO.GetComponent<UnityEngine.Rigidbody2D>();
                _clientFireForce = ball != null ? ball.FireForce : 400f;
                _clientBallMass = rb != null ? rb.mass : 1f;
                _clientGravityScale = ball != null ? ball.GravityScale : 1.2f;
                if (_clientGravityScale < 0)
                {
                    _clientGravityScale = -_clientGravityScale;
                }

                // Disable physics so ball doesn't fall
                if (rb != null)
                {
                    rb.simulated = false;
                }

                // Set as BattleController._activePachinkoBall so the native prediction
                // system can find it. Init + Arm set up PredictionManager for the aimer line.
                var activeBallField = HarmonyLib.AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
                activeBallField?.SetValue(bc, _clientBallGO);

                if (ball != null)
                {
                    // Initialize the ball with game systems for prediction rendering
                    var rm = Resources.FindObjectsOfTypeAll<Relics.RelicManager>()?.FirstOrDefault();
                    var pm = bc.PredictionManager;
                    var psec = UnityEngine.Object.FindObjectOfType<Battle.StatusEffects.PlayerStatusEffectController>();
                    ball.Init(rm, dm, UnityEngine.Vector2.right, pm, psec);
                    ball.InitializeMembers();

                    // Arm the ball — this enables TrajectorySimulation and prediction line
                    try
                    {
                        ball.Arm();
                    }
                    catch (System.Exception armEx)
                    {
                        MultiplayerPlugin.Logger?.LogWarning(
                            $"[ClientAim] Arm() failed (non-fatal): {armEx.GetType().Name}: " +
                            $"'{armEx.Message}' pm={(pm == null ? "null" : "ok")} " +
                            $"trajSim={(ball.GetComponent<TrajectorySimulation>() == null ? "null" : "ok")}\n{armEx.StackTrace}");
                    }

                    // Set AIMING state for proper mouse input handling in PachinkoBall.Update
                    var stateProp = HarmonyLib.AccessTools.Property(typeof(PachinkoBall), "CurrentState");
                    stateProp?.GetSetMethod(true)?.Invoke(ball, new object[] { PachinkoBall.FireballState.AIMING });
                }

                // SUCCESS — mark initialized so we don't retry
                _clientBallInitialized = true;
                _clientBallRetryCount = 0;

                MultiplayerPlugin.Logger?.LogInfo(
                    $"[ClientAim] Created ball at ({spawnPos.x:F1},{spawnPos.y:F1}), " +
                    $"fireForce={_clientFireForce}, mass={_clientBallMass}, gravity={_clientGravityScale}, " +
                    $"state={ball?.CurrentState}, active={_clientBallGO.activeInHierarchy}");
            }
            catch (System.Exception ex)
            {
                _clientBallRetryCount++;
                MultiplayerPlugin.Logger?.LogError($"[ClientAim] Failed to create ball (retry {_clientBallRetryCount}): {ex}");
                // Clean up partial creation
                if (_clientBallGO != null)
                {
                    UnityEngine.Object.Destroy(_clientBallGO);
                    _clientBallGO = null;
                }
                // DO NOT set _clientBallInitialized — allow retry on next frame
            }
        }

        // PachinkoBall.Update() runs natively — handles mouse aiming + trajectory rendering.
        // When the player clicks, PachinkoBall calls Fire() which is intercepted by
        // PachinkoBall_Fire_Prefix, which sends the ShootRequest to the host.
        // No manual mouse tracking needed here.
    }

    internal static bool IsPointerOverUI()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        return es != null && es.IsPointerOverGameObject();
    }

    // Cached lists/PED to avoid per-frame allocations in the click hot path.
    internal static readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> _uiRaycastBuf
        = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>(8);

    /// <summary>
    /// Returns true only if the cursor is over a clickable Button (Fire/Skip/Discard
    /// or any other UI Button). Returns false for non-interactive UI like the pegboard
    /// frame, mirror sprites, or other layout overlays — those should NOT block the
    /// fallback shoot path. This is the gate used for the fallback left-click → shot
    /// in MirrorPlantBattle and similar layouts where IsPointerOverGameObject() is
    /// permanently true even over the playfield.
    /// </summary>
    internal static bool IsPointerOverInteractiveUI()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null)
        {
            return false;
        }

        if (!es.IsPointerOverGameObject())
        {
            return false;
        }

        try
        {
            var ped = new UnityEngine.EventSystems.PointerEventData(es)
            {
                position = UnityEngine.Input.mousePosition,
            };
            _uiRaycastBuf.Clear();
            es.RaycastAll(ped, _uiRaycastBuf);
            for (var i = 0; i < _uiRaycastBuf.Count; i++)
            {
                var go = _uiRaycastBuf[i].gameObject;
                if (go == null)
                {
                    continue;
                }

                if (go.GetComponentInParent<UnityEngine.UI.Button>() != null)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    internal static readonly UnityEngine.Collider2D[] _enemyOverlapBuf = new UnityEngine.Collider2D[8];

    /// <summary>
    /// Returns true if the cursor is over a 2D collider belonging to an Enemy.
    /// Used to suppress the fallback left-click → shot path so target-selection
    /// clicks (clicking an enemy to set currentTarget) don't fire the player's shot.
    /// </summary>
    internal static bool IsPointerOverEnemy()
    {
        try
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                return false;
            }

            var world = cam.ScreenToWorldPoint(UnityEngine.Input.mousePosition);
            var point = new UnityEngine.Vector2(world.x, world.y);
            var count = UnityEngine.Physics2D.OverlapPointNonAlloc(point, _enemyOverlapBuf);
            for (var i = 0; i < count; i++)
            {
                var col = _enemyOverlapBuf[i];
                if (col == null)
                {
                    continue;
                }

                if (col.GetComponentInParent<Battle.Enemies.Enemy>() != null)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// Send a ShootRequestEvent computed directly from the camera + mouse position,
    /// bypassing PachinkoBall.LateUpdate's native click→Fire path. Used when the
    /// native path is broken (Arm failed, _predictionManager null, Camera.main
    /// races on scene load, etc). Mirrors Fire-prefix's send logic.
    /// </summary>
    internal static void TrySendDirectShot()
    {
        try
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                return;
            }

            var ballPos = _clientBallGO.transform.position;
            var worldMouse = cam.ScreenToWorldPoint(UnityEngine.Input.mousePosition);
            var aim = ((UnityEngine.Vector2)(worldMouse - ballPos)).normalized;
            if (aim.sqrMagnitude < 0.0001f)
            {
                return;
            }

            string targetGuid = null;
            try
            {
                var targetMgr = UnityEngine.Object.FindObjectOfType<Battle.TargetingManager>();
                var tservices = MultiplayerPlugin.Services;
                if (targetMgr?.currentTarget != null
                    && tservices?.TryResolve<Utility.EnemyIdentifier>(out var enemyId) == true)
                {
                    targetGuid = enemyId.GetGuid(targetMgr.currentTarget);
                }
            }
            catch
            {
            }

            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
            {
                sender.Send(new Events.Network.Coop.ShootRequestEvent
                {
                    AimDirectionX = aim.x,
                    AimDirectionY = aim.y,
                    TargetEnemyGuid = targetGuid,
                });
                ClientShotSentThisTurn = true;

                MultiplayerPlugin.Logger?.LogInfo(
                    $"[ClientAim] Direct click → ShootRequest: aim=({aim.x:F2},{aim.y:F2}), target={targetGuid ?? "auto"}");
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientAim] TrySendDirectShot failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Send the client's current aim direction to the host so it can render
    /// the aim line. Uses IMessageSender.Send() (client→host network path).
    /// </summary>
    internal static void SendClientAimUpdate()
    {
        if (_clientBallGO == null)
        {
            return;
        }

        var ball = _clientBallGO.GetComponent<PachinkoBall>();
        if (ball == null)
        {
            return;
        }

        var aimVec = ball.aimVector;
        if (aimVec == UnityEngine.Vector2.zero)
        {
            return;
        }

        var pos = ball.transform.position;
        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
        {
            sender.Send(new Events.Network.Ball.AimUpdateEvent
            {
                AimX = aimVec.x,
                AimY = aimVec.y,
                SpawnX = pos.x,
                SpawnY = pos.y,
            });
        }
    }

    /// <summary>
    /// Compute and draw a trajectory arc using the same formula as
    /// TrajectorySimulation.simulatePath(). Uses fixedDeltaTime for
    /// frame-rate independence instead of the game's Time.deltaTime.
    /// </summary>
    internal static void DrawClientTrajectory(UnityEngine.Vector3 start, UnityEngine.Vector2 aimDir)
    {
        const int segments = 20;
        const float segmentScale = 1f;

        var positions = new UnityEngine.Vector3[segments];
        positions[0] = start;

        // Initial velocity: matches TrajectorySimulation line 61
        // Original uses Time.deltaTime which varies per frame; we use fixedDeltaTime for stability
        var vel = (UnityEngine.Vector3)(aimDir * (_clientFireForce * UnityEngine.Time.fixedDeltaTime) / _clientBallMass);

        for (var i = 1; i < segments; i++)
        {
            var dt = vel.sqrMagnitude > 0f ? segmentScale / vel.magnitude : 0f;
            vel += UnityEngine.Physics.gravity * (_clientGravityScale * dt);
            positions[i] = positions[i - 1] + vel * dt;
        }

        _clientTrajectoryLR.positionCount = segments;
        _clientTrajectoryLR.SetPositions(positions);
    }

    /// <summary>
    /// Set to true by BattleController_Update_Postfix just before calling
    /// AttemptOrbDiscard() to execute a client's pending discard request. This
    /// prevents AttemptOrbDiscard_Prefix from blocking the programmatic discard.
    /// </summary>
    internal static bool _executingPendingDiscard;

    /// <summary>
    /// Recovery state for the host's pending-shot loop when _activePachinkoBall
    /// is unexpectedly null. Tracks the slot we're stuck on, when the stuck
    /// state began, and how many redraw retries we've attempted, so that
    /// BattleController_Update_Postfix can escalate from "warn and wait" to
    /// "retry DrawBall" to "skip the slot" rather than spamming a warning
    /// thousands of times until the round eventually times out.
    /// </summary>
    internal static int _stuckPendingShotSlot = -1;
    internal static float _stuckPendingShotSinceUnscaledTime;
    internal static int _stuckPendingShotRedraws;
    internal static float _stuckPendingShotLastWarnTime;

    /// <summary>
    /// Host-side watchdog state for AWAITING_SHOT_COMPLETION softlocks. The
    /// canonical case is SummoningCircle spawning a custom orb (Bob-Orb) whose
    /// satellites get caught in a Spirit-of-Radia black-hole gravity well: they
    /// stay above y=-15, never go below the lostBallY threshold, and keep their
    /// |velocity| above 0.25 indefinitely, so PachinkoBall.Update never calls
    /// StartDestroy() on them. _remainingPachinkoBalls stays > 0 forever and
    /// OnShotComplete never fires — turn manager, host, and clients all wait.
    ///
    /// _shotFiredUnscaledTime: stamp from BattleController.ShotFired postfix.
    /// _stuckCompletionSinceUnscaledTime: 0 when the last scan saw at least
    ///   one live FIRING non-dummy ball; the unscaledTime of the first scan to
    ///   see zero firing balls (with counter still > 0) otherwise. Sustained
    ///   for >5s triggers a force-unstick.
    /// _stuckCompletionScanTimer: throttle the FindObjectsOfType scan to ~2 Hz.
    /// </summary>
    internal static float _shotFiredUnscaledTime;
    internal static float _stuckCompletionSinceUnscaledTime;
    internal static float _stuckCompletionScanTimer;

    // =========================================================================
    // BLOCK CLIENT SCENE LOADS — only our sync handlers may load scenes
    // =========================================================================

    /// <summary>
    /// Block ALL scene loads on the client except those explicitly initiated by our
    /// sync system (NodeActivatedClientHandler, MapStateApplier). This prevents the
    /// game's own MapController/node flow from triggering a second Battle load after
    /// we've already loaded the correct scene.
    /// </summary>
    [HarmonyPatch(
        typeof(PeglinSceneLoader),
        nameof(PeglinSceneLoader.LoadScene),
        new[] { typeof(PeglinSceneLoader.Scene), typeof(UnityEngine.SceneManagement.LoadSceneMode), typeof(bool), typeof(float) })]
    [HarmonyPrefix]
    public static bool PeglinSceneLoader_LoadScene_Prefix(PeglinSceneLoader.Scene scene)
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (AllowNextSceneLoad)
        {
            AllowNextSceneLoad = false;
            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] ALLOWING scene load: {scene} (sync-initiated)");
            return true;
        }

        MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] BLOCKED scene load: {scene} (not sync-initiated)");
        return false;
    }

    /// <summary>
    /// After MapController.Awake wires up _nodes / _player, restore the last-known map
    /// state from cache so the first rendered frame of the scene already shows the
    /// correct node icons and player position. Without this the user sees ~50ms of
    /// default-state render + a camera snap when the first heartbeat apply arrives.
    /// Runs after Awake's own assignments but before Start, so _playerInitialPosition
    /// (also captured in Awake) can be overwritten here to reflect the cached position.
    /// </summary>
    [HarmonyPatch(typeof(Map.MapController), "Awake")]
    [HarmonyPostfix]
    public static void MapController_Awake_Postfix(Map.MapController __instance)
    {
        if (IsHosting)
        {
            return;
        }

        if (!ShouldSuppressClientLogic)
        {
            return;
        }

        if (__instance == null)
        {
            return;
        }
        // Only the surviving instance (the new scene's MC) should apply cached state —
        // the self-destruct path in the original Awake leaves the stale GO pending
        // destruction; skip it to avoid mutating doomed nodes.
        if (Map.MapController.instance != __instance)
        {
            return;
        }

        GameState.Appliers.MapStateApplier.ApplyCachedOnAwake(__instance, MultiplayerPlugin.Logger);
    }

    /// <summary>
    /// Block status effect application on client enemies. The host sends the correct
    /// status effects via heartbeat and the applier sets them directly. Without this,
    /// the client's own attack resolution keeps stacking effects every frame.
    /// The applier sets AllowStatusEffectSync=true while it's applying host effects.
    /// </summary>
    internal static bool AllowStatusEffectSync;

    /// <summary>
    /// Return a short string identifying the caller of a shuffle method.
    /// Walks up the stack past Harmony wrappers and the shuffle method itself,
    /// returning up to 3 user frames in "Type.Method > Type.Method" order.
    /// </summary>
    internal static string DescribeShuffleCaller()
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(2, false);
            var picked = new System.Collections.Generic.List<string>();
            for (var i = 0; i < trace.FrameCount && picked.Count < 3; i++)
            {
                var m = trace.GetFrame(i)?.GetMethod();
                if (m == null)
                {
                    continue;
                }

                var t = m.DeclaringType?.FullName ?? "?";
                // Skip Harmony-generated wrappers and this patch class
                if (t.StartsWith("HarmonyLib") || t.StartsWith("System.") ||
                    t.Contains("DMD<") || t.Contains("MultiplayerClientPatches"))
                {
                    continue;
                }

                picked.Add($"{m.DeclaringType?.Name ?? "?"}.{m.Name}");
            }

            return picked.Count == 0 ? "<unknown>" : string.Join(" > ", picked);
        }
        catch
        {
            return "<stacktrace-failed>";
        }
    }

    internal static IEnumerator EmptyEnumerator() { yield break; }

    /// <summary>
    /// Coroutine that plays each player's shot sequentially during the host's attack
    /// phase. For each slot with shot data we: dispatch an AttackStartedEvent so
    /// clients can mirror the visual, play the peglin throw animation on host,
    /// launch a visual-only projectile via ClientAttackProjectile, then apply damage
    /// on impact. Between shots we pause briefly so enemy damage animations have
    /// time to read. When the sequence ends we release AttackManager state so the
    /// BattleController state machine advances from ATTACKING.
    /// </summary>
    internal static System.Collections.IEnumerator PlayCoopAttackSequence(
        BattleController bc,
        Battle.Attacks.AttackManager am,
        System.Collections.Generic.List<Events.Subscriptions.Coop.PlayerAttackData> shots,
        EnemyManager em,
        Utility.EnemyIdentifier enemyId)
    {
        var services = MultiplayerPlugin.Services;
        IGameEventRegistry reg = null;
        services?.TryResolve<IGameEventRegistry>(out reg);

        // Sort by slot so playback order is stable and matches turn order.
        shots.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));

        foreach (var shot in shots)
        {
            if (shot.IsHeal && shot.Damage > 0)
            {
                ApplyCoopHeal(shot, em, enemyId, reg);
                yield return new UnityEngine.WaitForSeconds(0.3f);
                continue;
            }

            if (shot.Damage <= 0)
            {
                continue;
            }

            // Resolve targets (reuse existing targeting/pierce/raycast logic).
            Battle.Enemies.Enemy primaryTarget = null;
            var targets = new System.Collections.Generic.List<Battle.Enemies.Enemy>();

            if (shot.IsAoE)
            {
                foreach (var e in em.Enemies)
                {
                    if (e != null && e.CurrentHealth > 0f)
                    {
                        targets.Add(e);
                    }
                }

                if (targets.Count == 0)
                {
                    continue;
                }

                primaryTarget = targets[0];
            }
            else
            {
                if (!string.IsNullOrEmpty(shot.TargetEnemyGuid))
                {
                    primaryTarget = enemyId.Find(shot.TargetEnemyGuid);
                }

                if (primaryTarget == null || primaryTarget.CurrentHealth <= 0f)
                {
                    primaryTarget = em.GetFarthestEnemyFromPlayer();
                }

                if (primaryTarget == null)
                {
                    continue;
                }

                var pierceCount = GetOrbPierceCount(shot.OrbPrefabName);
                targets.Add(primaryTarget);
                if (pierceCount > 0)
                {
                    targets.AddRange(GetEnemiesBehindTarget(em, primaryTarget, pierceCount));
                }

                try
                {
                    var raycastTargets = ResolveShotTargetsViaRaycast(
                        bc, em, primaryTarget, shot.OrbPrefabName, pierceCount);
                    if (raycastTargets != null && raycastTargets.Count > 0)
                    {
                        // For pierce orbs (Sphear etc.), only let the raycast override the
                        // manually-built pierce list when it found at least as many targets.
                        // The raycast samples 3 thin rays and can miss narrow colliders on
                        // back-row enemies — without this guard, pierce gets clipped down to
                        // a single target whenever the lateral spread fails to land on the
                        // back enemies, even though they're sitting directly in line.
                        if (pierceCount == 0 || raycastTargets.Count >= targets.Count)
                        {
                            targets = raycastTargets;
                            primaryTarget = raycastTargets[0];
                        }
                    }
                }
                catch (System.Exception rex)
                {
                    MultiplayerPlugin.Logger?.LogWarning(
                        $"[CoopAttack] Raycast redirect failed for {shot.OrbPrefabName}: {rex.Message}");
                }

                ExpandTargetsForShooterRelics(em, shot, primaryTarget, targets);
            }

            // Resolve a GUID for the primary target so clients can find the same
            // enemy even if the shot was AoE (TargetEnemyGuid may be empty in that case).
            var primaryGuid = shot.TargetEnemyGuid;
            if (string.IsNullOrEmpty(primaryGuid) && primaryTarget != null)
            {
                primaryGuid = enemyId?.GetGuid(primaryTarget);
            }

            // Tell clients to play this shot's visual.
            try
            {
                reg?.Dispatch(new Events.Network.Battle.AttackStartedEvent
                {
                    AnimTrigger = "attack",
                    TargetEnemyGuid = primaryGuid,
                    NumPegsHit = shot.NumPegsHit,
                    IsCrit = shot.CriticalHitCount > 0,
                    OrbName = shot.OrbPrefabName,
                    SlotIndex = shot.SlotIndex,
                });
            }
            catch
            {
            }

            // Fire peglin throw animation on host (AttackStartedClientHandler does
            // the same on spectating clients). PeglinBattleAnimationController
            // subscribes to OnAttackPerformed, not OnPeglinAttackAnimationRequested —
            // the latter has no subscribers and won't drive OnFirePoint.
            try
            {
                Battle.Attacks.AttackManager.OnAttackPerformed?.Invoke("attack");
            }
            catch
            {
            }

            // Arm ClientAttackProjectile on host to fly the sprite when OnFirePoint fires.
            var cap = Multipeglin.GameState.ClientAttackProjectile.Instance;
            if (cap != null && !string.IsNullOrEmpty(primaryGuid))
            {
                cap.SetupAttack(primaryGuid, shot.NumPegsHit, shot.CriticalHitCount > 0, shot.OrbPrefabName);
            }

            // Wait until the projectile has flown and landed (or the watchdog fires).
            var waited = 0f;
            while (cap != null && cap.IsAttacking && waited < 2.0f)
            {
                waited += UnityEngine.Time.deltaTime;
                yield return null;
            }

            // Apply damage at "impact" — route attribution to the shot's owner so
            // OnEnemyDamaged credits damage to the correct player, not the currently
            // active singleton slot (which has already rotated to next round's lead).
            Events.Subscriptions.EnemySubscriptions.DamageAttributionSlotOverride = shot.SlotIndex;
            try
            {
                foreach (var t in targets)
                {
                    if (t == null || t.CurrentHealth <= 0f)
                    {
                        continue;
                    }

                    var src = shot.IsAoE
                        ? Battle.Enemies.Enemy.EnemyDamageSource.AOE
                        : Battle.Enemies.Enemy.EnemyDamageSource.TargetedAttack;
                    t.Damage(shot.Damage, screenshake: false, 0.25f, 1f, unblockable: false, src);
                    ApplyStatusEffectsToEnemy(t, shot.StatusEffectsToApply);
                }
            }
            finally
            {
                Events.Subscriptions.EnemySubscriptions.DamageAttributionSlotOverride = -1;
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopAttack] Played slot {shot.SlotIndex} ({shot.PlayerName}): " +
                $"orb={shot.OrbPrefabName} dmg={shot.Damage} targets={targets.Count} " +
                $"aoe={shot.IsAoE} effects={shot.StatusEffectsToApply?.Count ?? 0}");

            // Pincer Maneuver: fire a second visual + damage at the farthest enemy
            // with the same (already-halved) damage, mirroring ProjectileAttack.Fire.
            if (shot.HasReverseShot && !shot.IsAoE && !shot.IsHeal && shot.Damage > 0)
            {
                yield return new UnityEngine.WaitForSeconds(0.15f);

                var reverseTarget = em.GetFarthestEnemyFromPlayer();
                if (reverseTarget != null && reverseTarget.CurrentHealth > 0f)
                {
                    var reverseGuid = enemyId?.GetGuid(reverseTarget);

                    try
                    {
                        reg?.Dispatch(new Events.Network.Battle.AttackStartedEvent
                        {
                            AnimTrigger = "attack",
                            TargetEnemyGuid = reverseGuid,
                            NumPegsHit = shot.NumPegsHit,
                            IsCrit = shot.CriticalHitCount > 0,
                            OrbName = shot.OrbPrefabName,
                            SlotIndex = shot.SlotIndex,
                        });
                    }
                    catch
                    {
                    }

                    try
                    {
                        Battle.Attacks.AttackManager.OnAttackPerformed?.Invoke("attack");
                    }
                    catch
                    {
                    }

                    if (cap != null && !string.IsNullOrEmpty(reverseGuid))
                    {
                        cap.SetupAttack(reverseGuid, shot.NumPegsHit, shot.CriticalHitCount > 0, shot.OrbPrefabName);
                    }

                    var reverseWaited = 0f;
                    while (cap != null && cap.IsAttacking && reverseWaited < 2.0f)
                    {
                        reverseWaited += UnityEngine.Time.deltaTime;
                        yield return null;
                    }

                    var reverseTargets = new System.Collections.Generic.List<Battle.Enemies.Enemy> { reverseTarget };
                    ExpandTargetsForShooterRelics(em, shot, reverseTarget, reverseTargets);

                    Events.Subscriptions.EnemySubscriptions.DamageAttributionSlotOverride = shot.SlotIndex;
                    try
                    {
                        foreach (var rt in reverseTargets)
                        {
                            if (rt == null || rt.CurrentHealth <= 0f)
                            {
                                continue;
                            }

                            rt.Damage(
                                shot.Damage,
                                screenshake: false,
                                0.25f,
                                1f,
                                unblockable: false,
                                Battle.Enemies.Enemy.EnemyDamageSource.TargetedAttack);
                        }
                    }
                    finally
                    {
                        Events.Subscriptions.EnemySubscriptions.DamageAttributionSlotOverride = -1;
                    }

                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[CoopAttack] Pincer reverse for slot {shot.SlotIndex}: dmg={shot.Damage} target={reverseGuid} splashTargets={reverseTargets.Count}");
                }
            }

            // Brief gap between shots so the enemy flinch animation is visible
            // before the next orb is thrown.
            yield return new UnityEngine.WaitForSeconds(0.3f);
        }

        // Cleanup any temp orb from RestoreAttackFromPrefab and zero BC tallies so
        // a re-entry into DoAttack (e.g. bomb-throw resolution that replays
        // ALL_DONE and writes host tallies back) cannot cause the native pipeline
        // to replay the host's damage a second time.
        Events.Subscriptions.CoopSubscriptions.CleanupTempOrb();
        try
        {
            AccessTools.Field(typeof(BattleController), "_pegMultiplierDamageTally")?.SetValue(bc, 0);
            AccessTools.Field(typeof(BattleController), "_numPegsHit")?.SetValue(bc, 0);
            AccessTools.Field(typeof(BattleController), "_cactusDamageTally")?.SetValue(bc, 0);
            AccessTools.Field(typeof(BattleController), "_criticalHitCount")?.SetValue(null, 0);
            AccessTools.Field(typeof(BattleController), "_damageMultiplier")?.SetValue(bc, 1f);
            AccessTools.Field(typeof(BattleController), "_damageBonus")?.SetValue(bc, 0);
        }
        catch (System.Exception tallyEx)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopAttack] Failed to zero BC tallies post-sequence: {tallyEx.Message}");
        }

        // Release the ATTACKING state so BattleController.Update advances out of it.
        if (am != null)
        {
            try
            {
                am.AttackAnimationEnded();
            }
            catch
            {
                var isAttackingField = AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_isAttacking");
                var animFinishedField = AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_attackAnimationFinished");
                isAttackingField?.SetValue(am, false);
                animFinishedField?.SetValue(am, true);
            }
        }

        // Re-enable the delegate-driven dispatch for any non-coop attacks.
        SuppressOnAttackStartedDispatch = false;
    }

    /// <summary>
    /// Apply a Doctorb-style heal during coop playback. The native HealAction.Fire
    /// path is suppressed (DoAttack prefix skips the native pipeline in coop), so
    /// the heal must be applied manually here against the shooter's CoopPlayerState
    /// — not the active singleton, which has already rotated to the next round's
    /// lead by the time PlayCoopAttackSequence runs. Mirrors HealAction.DoHealAction:
    /// applies the heal, then optionally damages the targeted enemy or all enemies
    /// based on the orb's damage*Multiplier fields.
    /// </summary>
    internal static void ApplyCoopHeal(
        Events.Subscriptions.Coop.PlayerAttackData shot,
        EnemyManager em,
        Utility.EnemyIdentifier enemyId,
        IGameEventRegistry reg)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null
            || !services.TryResolve<Multipeglin.GameState.CoopStateManager>(out var coopMgr))
        {
            return;
        }

        var state = coopMgr.GetPlayerState(shot.SlotIndex);
        if (state == null)
        {
            return;
        }

        var before = state.CurrentHealth;
        var newHealth = UnityEngine.Mathf.Min(state.MaxHealth, before + shot.Damage);
        var actualHeal = newHealth - before;
        if (actualHeal <= 0f)
        {
            // Already at max — no heal applied, skip secondary damage too (matches
            // HealAction.DoHealAction which gates secondary damage on `num > 0f`).
            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopAttack] Heal slot {shot.SlotIndex} ({shot.PlayerName}): " +
                $"already at full hp ({before}/{state.MaxHealth}), no heal applied");
            return;
        }

        state.CurrentHealth = newHealth;

        // If this slot is currently active (its singletons are loaded), also push
        // the heal into the live PlayerHealthController so the host's HUD updates.
        // The active player at this point is already the next round's lead (state
        // saved/loaded happens between rounds), so this is usually a no-op for the
        // shooter — but if attribution somehow lined up, write through.
        if (coopMgr.ActivePlayerSlot == shot.SlotIndex)
        {
            try
            {
                var phc = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
                if (phc != null)
                {
                    var healthField = AccessTools.Field(typeof(PlayerHealthController), "_playerHealth");
                    var healthVar = healthField?.GetValue(phc) as FloatVariable;
                    healthVar?.Set(newHealth);
                }
            }
            catch
            {
            }
        }

        try
        {
            reg?.Dispatch(new Events.Network.Health.PlayerHealedEvent
            {
                Amount = actualHeal,
                RemainingHealth = newHealth,
                TargetSlotIndex = shot.SlotIndex,
            });
        }
        catch
        {
        }

        // Apply secondary damage from HealAction.damageTargetedEnemyMultiplier /
        // damageAllEnemiesMultiplier. Read off the orb prefab.
        var targetedMult = 0f;
        var allMult = 0f;
        try
        {
            var orbPrefab = Loading.AssetLoading.Instance?.GetOrbPrefab(shot.OrbPrefabName);
            var heal = orbPrefab?.GetComponent<HealAction>();
            if (heal != null)
            {
                targetedMult = heal.damageTargetedEnemyMultiplier;
                allMult = heal.damageAllEnemiesMultiplier;
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[CoopAttack] Failed to read HealAction multipliers for {shot.OrbPrefabName}: {ex.Message}");
        }

        if (targetedMult > 0f && em != null)
        {
            var dmg = UnityEngine.Mathf.RoundToInt(actualHeal * targetedMult);
            if (dmg > 0)
            {
                Battle.Enemies.Enemy primary = null;
                if (!string.IsNullOrEmpty(shot.TargetEnemyGuid))
                {
                    primary = enemyId?.Find(shot.TargetEnemyGuid);
                }

                if (primary == null || primary.CurrentHealth <= 0f)
                {
                    primary = em.GetFarthestEnemyFromPlayer();
                }

                if (primary != null && primary.CurrentHealth > 0f)
                {
                    Events.Subscriptions.EnemySubscriptions.DamageAttributionSlotOverride = shot.SlotIndex;
                    try
                    {
                        primary.Damage(
                            dmg,
                            screenshake: false,
                            0.25f,
                            1f,
                            unblockable: false,
                            Battle.Enemies.Enemy.EnemyDamageSource.TargetedAttack);
                    }
                    finally
                    {
                        Events.Subscriptions.EnemySubscriptions.DamageAttributionSlotOverride = -1;
                    }
                }
            }
        }
        else if (allMult > 0f && em != null)
        {
            var dmg = UnityEngine.Mathf.RoundToInt(actualHeal * allMult);
            if (dmg > 0)
            {
                Events.Subscriptions.EnemySubscriptions.DamageAttributionSlotOverride = shot.SlotIndex;
                try
                {
                    foreach (var e in em.Enemies)
                    {
                        if (e != null && e.CurrentHealth > 0f)
                        {
                            e.Damage(
                                dmg,
                                screenshake: false,
                                0.25f,
                                1f,
                                unblockable: false,
                                Battle.Enemies.Enemy.EnemyDamageSource.AOE);
                        }
                    }
                }
                finally
                {
                    Events.Subscriptions.EnemySubscriptions.DamageAttributionSlotOverride = -1;
                }
            }
        }

        MultiplayerPlugin.Logger?.LogInfo(
            $"[CoopAttack] Heal slot {shot.SlotIndex} ({shot.PlayerName}): " +
            $"orb={shot.OrbPrefabName} healed={actualHeal} ({before}→{newHealth}/{state.MaxHealth}) " +
            $"targetedMult={targetedMult} allMult={allMult}");
    }

    /// <summary>
    /// Apply captured status effects from a non-host player's orb to a target enemy.
    /// This replicates the status effect application that the normal attack pipeline
    /// does via IAffectEnemyOnHit components and Attack.GetStatusEffects().
    /// </summary>
    internal static void ApplyStatusEffectsToEnemy(
        Battle.Enemies.Enemy enemy,
        System.Collections.Generic.List<(Battle.StatusEffects.StatusEffectType Type, int Intensity)> effects)
    {
        if (effects == null || effects.Count == 0)
        {
            return;
        }

        if (enemy == null || enemy.CurrentHealth <= 0f)
        {
            return;
        }

        foreach (var (type, intensity) in effects)
        {
            if (type == Battle.StatusEffects.StatusEffectType.None)
            {
                continue;
            }

            try
            {
                enemy.ApplyStatusEffect(
                    new Battle.StatusEffects.StatusEffect(type, intensity),
                    Battle.StatusEffects.StatusEffectSource.PLAYER);
            }
            catch (System.Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopAttack] Failed to apply {type}({intensity}) to {enemy.name}: {ex.Message}");
            }
        }
    }

    internal static readonly System.Collections.Generic.Dictionary<string, int> _orbPierceCache
        = new System.Collections.Generic.Dictionary<string, int>();

    /// <summary>
    /// Apply shooter-owned splash relics (Alien's Rock = SPLASH_EFFECT_ON_TARGETED_ATTACKS,
    /// TARGETED_ATTACKS_HIT_ALL) to a coop-replayed targeted shot. We capture these
    /// flags at OnShotComplete on the host while the shooter's RelicManager is loaded;
    /// here we just expand the resolved target list. Mirrors TargetedAttack.HandleSpellHit.
    /// </summary>
    internal static void ExpandTargetsForShooterRelics(
        EnemyManager em,
        Events.Subscriptions.Coop.PlayerAttackData shot,
        Battle.Enemies.Enemy primaryTarget,
        System.Collections.Generic.List<Battle.Enemies.Enemy> targets)
    {
        if (em == null || shot == null || primaryTarget == null)
        {
            return;
        }

        if (shot.IsAoE)
        {
            return;
        }

        if (!shot.HasTargetedSplash && !shot.HasTargetedHitAll)
        {
            return;
        }

        try
        {
            if (shot.HasTargetedHitAll)
            {
                foreach (var e in em.Enemies)
                {
                    if (e == null || e.CurrentHealth <= 0f)
                    {
                        continue;
                    }

                    if (!targets.Contains(e))
                    {
                        targets.Add(e);
                    }
                }

                return;
            }

            bool isStationary;
            var slotIdx = em.GetSlotIndexForEnemy(primaryTarget, out isStationary);
            var slotType = em.GetSlotForEnemy(primaryTarget);
            var splash = em.GetSplashRangeEnemies(slotIdx, slotType, 1, Battle.Attacks.AoeAttack.AoeType.SIDE);
            if (splash == null)
            {
                return;
            }

            foreach (var e in splash)
            {
                if (e == null || e.CurrentHealth <= 0f)
                {
                    continue;
                }

                if (!targets.Contains(e))
                {
                    targets.Add(e);
                }
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopAttack] ExpandTargetsForShooterRelics failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads ShotBehavior._shotType / _enemiesToPierce off the orb prefab's
    /// serialized shot prefab so coop damage replay preserves pierce behavior
    /// (Sphear etc.) without running the physics pipeline. Zero = not pierce.
    /// </summary>
    internal static int GetOrbPierceCount(string orbPrefabName)
    {
        if (string.IsNullOrEmpty(orbPrefabName))
        {
            return 0;
        }

        if (_orbPierceCache.TryGetValue(orbPrefabName, out var cached))
        {
            return cached;
        }

        var result = 0;
        try
        {
            var orbPrefab = Loading.AssetLoading.Instance?.GetOrbPrefab(orbPrefabName);
            if (orbPrefab != null)
            {
                var projAttack = orbPrefab.GetComponent<Battle.Attacks.ProjectileAttack>();
                if (projAttack != null)
                {
                    var shotPrefabField = AccessTools.Field(typeof(Battle.Attacks.ProjectileAttack), "_shotPrefab");
                    var shotPrefabGO = shotPrefabField?.GetValue(projAttack) as UnityEngine.GameObject;
                    if (shotPrefabGO != null)
                    {
                        var sb = shotPrefabGO.GetComponent<Battle.Attacks.ShotBehavior>();
                        if (sb != null)
                        {
                            var typeField = AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_shotType");
                            var countField = AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_enemiesToPierce");
                            var shotType = (Battle.Attacks.ShotBehavior.ShotType)(typeField?.GetValue(sb) ?? 0);
                            var enemiesToPierce = (int)(countField?.GetValue(sb) ?? 0);
                            if (shotType == Battle.Attacks.ShotBehavior.ShotType.PIERCE)
                            {
                                result = enemiesToPierce;
                            }
                        }
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopAttack] GetOrbPierceCount({orbPrefabName}) failed: {ex.Message}");
        }

        _orbPierceCache[orbPrefabName] = result;
        return result;
    }

    /// <summary>
    /// Living enemies at slot indices behind the target (farther from the player),
    /// sorted nearest-first so pierce chains through them in order. Used to
    /// simulate pierce for replayed shots that bypass physics.
    /// </summary>
    internal static System.Collections.Generic.List<Battle.Enemies.Enemy> GetEnemiesBehindTarget(
        EnemyManager em, Battle.Enemies.Enemy target, int count)
    {
        var result = new System.Collections.Generic.List<Battle.Enemies.Enemy>();
        if (em == null || target == null || count <= 0)
        {
            return result;
        }

        float targetSlot;
        try
        {
            targetSlot = em.GetSlotIndexForEnemy(target, out var _);
        }
        catch
        {
            return result;
        }

        var candidates = new System.Collections.Generic.List<(Battle.Enemies.Enemy e, float slot)>();
        foreach (var e in em.Enemies)
        {
            if (e == null || e == target || e.CurrentHealth <= 0f)
            {
                continue;
            }

            float slot;
            try
            {
                slot = em.GetSlotIndexForEnemy(e, out var _);
            }
            catch
            {
                continue;
            }

            if (slot > targetSlot)
            {
                candidates.Add((e, slot));
            }
        }

        candidates.Sort((a, b) => a.slot.CompareTo(b.slot));

        for (var i = 0; i < candidates.Count && result.Count < count; i++)
        {
            result.Add(candidates[i].e);
        }

        return result;
    }

    internal struct OrbShotInfo
    {
        public Battle.Attacks.ShotBehavior.ShotType ShotType;
        public bool CanAimUp;
        public int EnemiesToPierce;
        public bool Valid;
    }

    internal static readonly System.Collections.Generic.Dictionary<string, OrbShotInfo> _orbShotInfoCache
        = new System.Collections.Generic.Dictionary<string, OrbShotInfo>();

    internal static OrbShotInfo GetOrbShotInfo(string orbPrefabName)
    {
        if (string.IsNullOrEmpty(orbPrefabName))
        {
            return default;
        }

        if (_orbShotInfoCache.TryGetValue(orbPrefabName, out var cached))
        {
            return cached;
        }

        var info = default(OrbShotInfo);
        try
        {
            var orbPrefab = Loading.AssetLoading.Instance?.GetOrbPrefab(orbPrefabName);
            var projAttack = orbPrefab?.GetComponent<Battle.Attacks.ProjectileAttack>();
            var shotPrefabField = AccessTools.Field(typeof(Battle.Attacks.ProjectileAttack), "_shotPrefab");
            var shotPrefabGO = shotPrefabField?.GetValue(projAttack) as UnityEngine.GameObject;
            var sb = shotPrefabGO?.GetComponent<Battle.Attacks.ShotBehavior>();
            if (sb != null)
            {
                var typeField = AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_shotType");
                var pierceField = AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_enemiesToPierce");
                var aimField = AccessTools.Field(typeof(Battle.Attacks.ShotBehavior), "_canAimUp");
                info.ShotType = (Battle.Attacks.ShotBehavior.ShotType)(typeField?.GetValue(sb) ?? 0);
                info.EnemiesToPierce = (int)(pierceField?.GetValue(sb) ?? 0);
                info.CanAimUp = (bool)(aimField?.GetValue(sb) ?? true);
                info.Valid = true;
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopAttack] GetOrbShotInfo({orbPrefabName}) failed: {ex.Message}");
        }

        _orbShotInfoCache[orbPrefabName] = info;
        return info;
    }

    /// <summary>
    /// Replicates ShotBehavior.RaycastShotFlight so coop non-host damage respects
    /// line-of-sight: front enemies block NORMAL orbs from reaching back-row targets.
    /// Returns the ordered list of enemies that would actually be hit (closest first),
    /// or null if the raycast found nothing / the caller should keep its fallback.
    /// PINPOINT orbs intentionally ignore blockers — returns null to preserve that.
    /// </summary>
    internal static System.Collections.Generic.List<Battle.Enemies.Enemy> ResolveShotTargetsViaRaycast(
        BattleController bc,
        EnemyManager em,
        Battle.Enemies.Enemy declaredTarget,
        string orbPrefabName,
        int pierceCount)
    {
        if (bc == null || em == null || declaredTarget == null)
        {
            return null;
        }

        var info = GetOrbShotInfo(orbPrefabName);
        if (info.Valid && info.ShotType == Battle.Attacks.ShotBehavior.ShotType.PINPOINT)
        {
            return null;
        }

        var playerField = AccessTools.Field(typeof(BattleController), "_playerTransform");
        var playerTransform = playerField?.GetValue(bc) as UnityEngine.Transform;
        if (playerTransform == null)
        {
            return null;
        }

        var offsetField = AccessTools.Field(typeof(BattleController), "_playerTransformOffset");
        var offset = (UnityEngine.Vector3)(offsetField?.GetValue(bc) ?? new UnityEngine.Vector3(1f, 0.5f, 0f));
        var origin = (UnityEngine.Vector2)(playerTransform.position + offset);

        var aim = ((UnityEngine.Vector2)declaredTarget.transform.position - origin).normalized;
        if (aim.sqrMagnitude < 0.0001f)
        {
            return null;
        }

        UnityEngine.Vector2 perp = UnityEngine.Vector3.Cross(aim, UnityEngine.Vector3.back).normalized;
        const float lateralOffset = 0.08f;
        var hits = new System.Collections.Generic.List<UnityEngine.RaycastHit2D>();
        hits.AddRange(UnityEngine.Physics2D.RaycastAll(origin, aim, 50f));
        hits.AddRange(UnityEngine.Physics2D.RaycastAll(origin + perp * lateralOffset, aim, 50f));
        hits.AddRange(UnityEngine.Physics2D.RaycastAll(origin - perp * lateralOffset, aim, 50f));

        var canAimUp = !info.Valid || info.CanAimUp;
        var byEnemy = new System.Collections.Generic.Dictionary<Battle.Enemies.Enemy, float>();
        foreach (var h in hits)
        {
            if (h.collider == null)
            {
                continue;
            }

            if (!h.collider.TryGetComponent<Battle.Enemies.Enemy>(out var e))
            {
                continue;
            }

            if (e == null || e.CurrentHealth <= 0f)
            {
                continue;
            }

            if (byEnemy.ContainsKey(e))
            {
                continue;
            }
            // Mirror ShotBehavior filter: when canAimUp match flying==flying;
            // when !canAimUp (ground-only aim) skip flying enemies entirely.
            var flyingOk = canAimUp
                ? declaredTarget.IsFlying == e.IsFlying
                : !e.IsFlying;
            if (!flyingOk)
            {
                continue;
            }

            byEnemy[e] = h.distance;
        }

        if (byEnemy.Count == 0)
        {
            return null;
        }

        var ordered = new System.Collections.Generic.List<Battle.Enemies.Enemy>(byEnemy.Keys);
        ordered.Sort((a, b) => byEnemy[a].CompareTo(byEnemy[b]));

        var keep = System.Math.Max(1, pierceCount + 1);
        if (ordered.Count > keep)
        {
            ordered.RemoveRange(keep, ordered.Count - keep);
        }

        return ordered;
    }

    // =========================================================================
    // ATTACK ANIMATION DATA — capture attack trigger and target for sync
    // =========================================================================

    /// <summary>Stores the last attack animation trigger for the AttackStartedEvent.</summary>
    internal static string LastAttackAnimTrigger;
    internal static string LastAttackTargetGuid;
    internal static int LastAttackNumPegsHit;
    internal static bool LastAttackIsCrit;
    internal static string LastAttackOrbName;

    /// <summary>
    /// When true, BattleSubscriptions.OnAttackStarted suppresses its
    /// AttackStartedEvent dispatch. Set by the coop DoAttack coroutine so the
    /// generic delegate-driven dispatch doesn't duplicate the per-slot events
    /// that the coroutine emits itself.
    /// </summary>
    internal static bool SuppressOnAttackStartedDispatch;

    // =========================================================================
    // RNG SERIALIZATION HELPERS
    // =========================================================================

    internal static string SerializeRandomState(Random.State state)
    {
        try
        {
            var t = typeof(Random.State);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            object boxed = state;
            var s0 = (int)t.GetField("s0", flags).GetValue(boxed);
            var s1 = (int)t.GetField("s1", flags).GetValue(boxed);
            var s2 = (int)t.GetField("s2", flags).GetValue(boxed);
            var s3 = (int)t.GetField("s3", flags).GetValue(boxed);
            return $"{s0},{s1},{s2},{s3}";
        }
        catch
        {
            return null;
        }
    }

    internal static Random.State? DeserializeRandomState(string s)
    {
        try
        {
            if (string.IsNullOrEmpty(s))
            {
                return null;
            }

            var parts = s.Split(',');
            if (parts.Length != 4)
            {
                return null;
            }

            Random.InitState(0);
            var state = Random.state;
            var t = typeof(Random.State);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            object boxed = state;
            t.GetField("s0", flags).SetValue(boxed, int.Parse(parts[0]));
            t.GetField("s1", flags).SetValue(boxed, int.Parse(parts[1]));
            t.GetField("s2", flags).SetValue(boxed, int.Parse(parts[2]));
            t.GetField("s3", flags).SetValue(boxed, int.Parse(parts[3]));
            return (Random.State)boxed;
        }
        catch
        {
            return null;
        }
    }

    // =========================================================================
    // BLOCK CLIENT STATE-ALTERING METHODS — prevent state divergence
    // =========================================================================

    /// <summary>
    /// Set to true by sync code while applying host relic state.
    /// </summary>
    internal static bool AllowRelicSync;

    /// <summary>
    /// Set to true by sync code while applying host gold state.
    /// </summary>
    internal static bool AllowCurrencySync;

    /// <summary>
    /// Capture the client's current state after the reward screen for sending to host.
    /// </summary>
    internal static Events.Network.Coop.PostBattleCompleteEvent CaptureClientPostBattleState()
    {
        var evt = new Events.Network.Coop.PostBattleCompleteEvent();

        // Health
        var ctrl = UnityEngine.Object.FindObjectOfType<Battle.PlayerHealthController>();
        if (ctrl != null)
        {
            var healthField = HarmonyLib.AccessTools.Field(typeof(Battle.PlayerHealthController), "_playerHealth");
            var maxHealthField = HarmonyLib.AccessTools.Field(typeof(Battle.PlayerHealthController), "_maxPlayerHealth");
            var healthVar = healthField?.GetValue(ctrl) as FloatVariable;
            var maxHealthVar = maxHealthField?.GetValue(ctrl) as FloatVariable;
            evt.CurrentHealth = healthVar?.Value ?? 0;
            evt.MaxHealth = maxHealthVar?.Value ?? 0;
        }

        // Gold
        var currency = Currency.CurrencyManager.Instance;
        if (currency != null)
        {
            evt.Gold = currency.GoldAmount;
        }

        // Deck
        var completeDeck = DeckManager.completeDeck;
        if (completeDeck != null)
        {
            foreach (var orbGO in completeDeck)
            {
                if (orbGO == null)
                {
                    continue;
                }

                var attack = orbGO.GetComponent<Battle.Attacks.Attack>();
                evt.CompleteDeck.Add(new Events.Network.Coop.PostBattleOrbEntry
                {
                    PrefabName = orbGO.name,
                    Level = attack != null ? attack.Level : 0,
                });
            }
        }

        // Boss/rare relic chosen during this post-battle phase
        evt.ChosenRelicEffect = ClientChosenPostBattleRelicEffect;
        evt.ChosenRelicName = ClientChosenPostBattleRelicName;

        MultiplayerPlugin.Logger?.LogInfo(
            $"[ClientPatches] Captured post-battle state: HP={evt.CurrentHealth}/{evt.MaxHealth} Gold={evt.Gold} " +
            $"Deck={evt.CompleteDeck.Count} orbs, chosenRelic={evt.ChosenRelicEffect} ({evt.ChosenRelicName ?? "none"})");

        return evt;
    }

    // =========================================================================
    // CLIENT: TEXT SCENARIO SPECTATING — block dialogue + navigation on client
    // =========================================================================

    /// <summary>
    /// Flag to allow mirror event logic on the client (deck modification).
    /// </summary>
    public static bool AllowMirrorEventLogic;

    /// <summary>
    /// Captures the client's current deck/health/gold/relics after a TextScenario
    /// dialogue completes and sends a TextScenarioCompleteEvent to the host.
    /// </summary>
    internal static void CaptureAndSendTextScenarioState()
    {
        if (Events.Handlers.Coop.CoopRewardState.ClientTextScenarioChoiceSent)
        {
            return;
        }

        try
        {
            AllowTextScenarioLogic = false;
            Events.Handlers.Coop.CoopRewardState.ClientTextScenarioChoiceSent = true;

            var evt = new Events.Network.Scenarios.TextScenarioCompleteEvent();

            // Capture complete deck
            Utility.OrbIdentifier orbId = null;
            MultiplayerPlugin.Services?.TryResolve(out orbId);
            if (DeckManager.completeDeck != null)
            {
                foreach (var orbGo in DeckManager.completeDeck)
                {
                    if (orbGo == null)
                    {
                        continue;
                    }

                    var attack = orbGo.GetComponent<Battle.Attacks.Attack>();
                    var prefabName = orbGo.name.Replace("(Clone)", string.Empty).Trim();
                    var guid = orbId?.GetGuid(orbGo);
                    evt.CompleteDeck.Add(new GameState.SerializedOrb
                    {
                        PrefabName = prefabName,
                        Guid = guid ?? System.Guid.NewGuid().ToString(),
                        Level = attack?.Level ?? 1,
                    });
                }
            }

            // Capture health
            var healthControllers = UnityEngine.Resources.FindObjectsOfTypeAll<PlayerHealthController>();
            foreach (var hc in healthControllers)
            {
                if (hc != null)
                {
                    evt.CurrentHealth = hc.CurrentHealth;
                    evt.MaxHealth = hc.MaxHealth;
                    break;
                }
            }

            // Capture gold
            evt.Gold = Currency.CurrencyManager.Instance?.GoldAmount ?? 0;

            // Capture relics via reflection (RelicManager._ownedRelics is private)
            var relicManagers = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
            var ownedRelicsField = HarmonyLib.AccessTools.Field(typeof(Relics.RelicManager), "_ownedRelics");
            foreach (var rm in relicManagers)
            {
                if (rm == null)
                {
                    continue;
                }

                var ownedDict = ownedRelicsField?.GetValue(rm)
                    as System.Collections.Generic.Dictionary<Relics.RelicEffect, Relics.Relic>;
                if (ownedDict == null)
                {
                    continue;
                }

                foreach (var kvp in ownedDict)
                {
                    var relic = kvp.Value;
                    if (relic == null)
                    {
                        continue;
                    }

                    evt.Relics.Add(new GameState.SerializedRelic
                    {
                        Effect = (int)relic.effect,
                        LocKey = relic.locKey,
                        Rarity = (int)relic.globalRarity,
                    });
                }

                break;
            }

            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
            {
                sender.Send(evt);
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[ClientPatch] Sent TextScenarioCompleteEvent: deck={evt.CompleteDeck.Count}, " +
                    $"hp={evt.CurrentHealth}/{evt.MaxHealth}, gold={evt.Gold}, relics={evt.Relics.Count}");
            }

            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
            // Reset stale AllChoicesComplete from a prior phase so CoopRewardUI
            // doesn't early-return and keep the overlay hidden.
            Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = false;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[ClientPatch] Failed to send TextScenarioCompleteEvent: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Flag set by the state applier when the heartbeat reports IsNavigating=true.
    /// Allows the client to activate its navigation controller.
    /// </summary>
    public static bool AllowTextScenarioNavigation;

    /// <summary>
    /// Client-side: tracks purchases made during the current shop visit.
    /// Populated by PurchaseItem postfix, sent to host on CloseStore.
    /// </summary>
    internal static readonly System.Collections.Generic.List<Events.Network.Scenarios.ShopPurchase> ClientShopPurchases
        = new System.Collections.Generic.List<Events.Network.Scenarios.ShopPurchase>();

    /// <summary>Client-side: gold at the time the shop was entered.</summary>
    internal static int ClientShopStartGold;

    // =========================================================================
    // Block client-side bomb throwing (THROW_BOMBS state machine)
    // =========================================================================

    internal static readonly System.Reflection.FieldInfo _bombsRegularField =
        AccessTools.Field(typeof(BattleController), "_bombsToThrowRegular");

    internal static readonly System.Reflection.FieldInfo _bombsRiggedField =
        AccessTools.Field(typeof(BattleController), "_bombsToThrowRigged");

    internal static IEnumerator EmptyCoroutine()
    {
        yield break;
    }

    // =========================================================================
    // CLIENT: override Take-Relic UIs to roll a single random local relic
    //
    // During Treasure and TextScenario-relic events, the client's RelicManager
    // queues are not seeded the same way as the host's (the host seeds via RNG
    // at run init, which is blocked on the client). This causes the native UIs
    // to show 5 copies of a single broken "Blastic Powder" fallback relic.
    //
    // Per product direction: the client picks their own random relic. If they
    // accept it, it's added to their local RelicManager and synced up to host
    // via TreasureCompleteEvent / TextScenarioCompleteEvent. Host and client
    // don't need the SAME relic — each player picks their own.
    // =========================================================================

    /// <summary>Set of RelicEffects the player already owns (so we don't offer dupes).</summary>
    internal static System.Collections.Generic.HashSet<int> GetOwnedEffects(Relics.RelicManager rm)
    {
        var owned = new System.Collections.Generic.HashSet<int>();
        if (rm == null)
        {
            return owned;
        }

        try
        {
            var ownedField = AccessTools.Field(typeof(Relics.RelicManager), "_ownedRelics");
            var dict = ownedField?.GetValue(rm)
                as System.Collections.Generic.Dictionary<Relics.RelicEffect, Relics.Relic>;
            if (dict != null)
            {
                foreach (var k in dict.Keys)
                {
                    owned.Add((int)k);
                }
            }
        }
        catch
        {
        }

        return owned;
    }

    /// <summary>Pick a random Relic prefab matching <paramref name="rarity"/> that isn't already owned.
    /// When <paramref name="slotIndex"/> is non-negative, uses a slot-keyed deterministic
    /// System.Random instead of UnityEngine.Random so each player picks a different relic
    /// even when no host broadcast has arrived yet (UnityEngine.Random shares seed across
    /// all clients, which causes everyone to roll the same relic).</summary>
    internal static Relics.Relic PickRandomLocalRelic(Relics.RelicRarity rarity, Relics.RelicManager rm, int slotIndex = -1)
    {
        try
        {
            var owned = GetOwnedEffects(rm);
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.Relic>();
            // Relic is a ScriptableObject, so all returned are assets (no scene filtering needed).
            var candidates = new System.Collections.Generic.List<Relics.Relic>();
            foreach (var r in all)
            {
                if (r == null)
                {
                    continue;
                }

                if (r.globalRarity != rarity)
                {
                    continue;
                }

                if (owned.Contains((int)r.effect))
                {
                    continue;
                }

                candidates.Add(r);
            }

            if (candidates.Count == 0)
            {
                // Fallback: allow any rarity if pool empty
                foreach (var r in all)
                {
                    if (r == null)
                    {
                        continue;
                    }

                    if (owned.Contains((int)r.effect))
                    {
                        continue;
                    }

                    candidates.Add(r);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }
            // Stable order so the slot-keyed RNG produces consistent results across runs.
            candidates.Sort((a, b) => string.CompareOrdinal(a?.name, b?.name));
            int pick;
            if (slotIndex >= 0)
            {
                var seed = unchecked((StaticGameData.currentSeed ?? string.Empty).GetHashCode() ^ (slotIndex * 7919) ^ ((int)rarity * 31) ^ (StaticGameData.totalFloorCount * 104729));
                pick = new System.Random(seed).Next(0, candidates.Count);
            }
            else
            {
                pick = UnityEngine.Random.Range(0, candidates.Count);
            }

            return candidates[pick];
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] PickRandomLocalRelic failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Pick N unique relics matching <paramref name="rarity"/> the player doesn't own.
    /// Uses slot-keyed RNG so each player sees a different set even though the underlying
    /// seeded relic queue would otherwise produce identical first-N sequences across all
    /// clients (the queue never advances on clients because the host runs all battles).</summary>
    internal static System.Collections.Generic.List<Relics.Relic> PickMultipleLocalRelics(
        Relics.RelicRarity rarity, int count, Relics.RelicManager rm, int slotIndex)
    {
        var picked = new System.Collections.Generic.List<Relics.Relic>();
        try
        {
            if (count <= 0)
            {
                return picked;
            }

            var owned = GetOwnedEffects(rm);
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.Relic>();
            var candidates = new System.Collections.Generic.List<Relics.Relic>();
            foreach (var r in all)
            {
                if (r == null)
                {
                    continue;
                }

                if (r.globalRarity != rarity)
                {
                    continue;
                }

                if (owned.Contains((int)r.effect))
                {
                    continue;
                }

                candidates.Add(r);
            }

            if (candidates.Count == 0)
            {
                foreach (var r in all)
                {
                    if (r == null)
                    {
                        continue;
                    }

                    if (owned.Contains((int)r.effect))
                    {
                        continue;
                    }

                    candidates.Add(r);
                }
            }

            if (candidates.Count == 0)
            {
                return picked;
            }

            candidates.Sort((a, b) => string.CompareOrdinal(a?.name, b?.name));

            var seed = unchecked((StaticGameData.currentSeed ?? string.Empty).GetHashCode()
                ^ (slotIndex * 7919)
                ^ ((int)rarity * 31)
                ^ (StaticGameData.totalFloorCount * 104729));
            var rng = new System.Random(seed);
            // Fisher-Yates partial shuffle for the first `count` slots.
            var take = System.Math.Min(count, candidates.Count);
            for (var i = 0; i < take; i++)
            {
                var j = i + rng.Next(0, candidates.Count - i);
                var tmp = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = tmp;
                picked.Add(candidates[i]);
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] PickMultipleLocalRelics failed: {ex.Message}");
        }

        return picked;
    }

    /// <summary>Find a Relic ScriptableObject by name. Returns null if not loaded.</summary>
    internal static Relics.Relic FindRelicByName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var all = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.Relic>();
        foreach (var r in all)
        {
            if (r != null && r.name == name)
            {
                return r;
            }
        }

        return null;
    }

    internal static System.Collections.Generic.List<GameObject> ResolveOrbPrefabsByName(
        System.Collections.Generic.List<string> names, DeckManager deckMgr)
    {
        var result = new System.Collections.Generic.List<GameObject>();
        var allPools = new[] { deckMgr.CommonOrbPool, deckMgr.UncommonOrbPool, deckMgr.RareOrbPool };
        foreach (var name in names)
        {
            GameObject found = null;
            foreach (var pool in allPools)
            {
                if (pool == null)
                {
                    continue;
                }

                foreach (var go in pool)
                {
                    if (go != null && go.name == name)
                    {
                        found = go;
                        break;
                    }
                }

                if (found != null)
                {
                    break;
                }
            }

            if (found == null)
            {
                // Fall back: scan all loaded Attack prefabs for a name match.
                foreach (var attack in Resources.FindObjectsOfTypeAll<Battle.Attacks.Attack>())
                {
                    if (attack == null || attack.gameObject == null)
                    {
                        continue;
                    }

                    if (attack.gameObject.name == name)
                    {
                        found = attack.gameObject;
                        break;
                    }
                }
            }

            if (found != null)
            {
                result.Add(found);
            }
        }

        return result;
    }

    // --- SpiritOfRadia phase-2 transition: host signals client, client mirrors visuals ---
    //
    // Host: AI fires the boss's StartPhase2PreTransition coroutine, which after a delay
    // invokes PreTransitionStarted (cracks walls, hides floor/crystals, fades, moves
    // boss). That coroutine ends with StartCoroutine(boss.StartPhase2Transition()),
    // which fires OnSpiritOfRadiaPhaseTransitionStarted (clears roof/floor, shows void
    // walls, moves UI). On the host we hook those two delegates to dispatch a network
    // event with Step=1 / Step=2.
    //
    // Client: Act4BossPegBoardFrameManager.RegisterCallbacks runs from BattleController.Start
    // on both sides, so the visual subscribers exist on the client. The client handler
    // for the network event invokes the same static delegate, triggering the same visual
    // coroutines. We block the boss's own StartPhase2Transition IEnumerator on the
    // client because Act4BossPegBoardFrameManager.Phase2PreTransition (which does run
    // on client when PreTransitionStarted fires) ends with StartCoroutine(boss.StartPhase2Transition())
    // — that's host-authoritative state we don't want re-running on the client.

    internal static bool _spiritRadiaHostSubscribed;

    internal static void DispatchPreTransition()
    {
        try
        {
            if (MultiplayerPlugin.Services == null)
            {
                return;
            }

            if (!MultiplayerPlugin.Services.TryResolve<IGameEventRegistry>(out var registry))
            {
                return;
            }

            MultiplayerPlugin.Logger?.LogInfo("[SpiritOfRadia] Host: dispatching Step=1 (pre-transition)");
            registry.Dispatch(new Multipeglin.Events.Network.Battle.SpiritOfRadiaPhaseTransitionEvent { Step = 1 });
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SpiritOfRadia] DispatchPreTransition failed: {e.Message}");
        }
    }

    internal static void DispatchMainTransition()
    {
        try
        {
            if (MultiplayerPlugin.Services == null)
            {
                return;
            }

            if (!MultiplayerPlugin.Services.TryResolve<IGameEventRegistry>(out var registry))
            {
                return;
            }

            MultiplayerPlugin.Logger?.LogInfo("[SpiritOfRadia] Host: dispatching Step=2 (main transition)");
            registry.Dispatch(new Multipeglin.Events.Network.Battle.SpiritOfRadiaPhaseTransitionEvent { Step = 2 });
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SpiritOfRadia] DispatchMainTransition failed: {e.Message}");
        }
    }
}
