using System;
using System.Collections;
using System.Linq;
using Battle;
using Data;
using HarmonyLib;
using I2.Loc;
using Loading;
using Map;
using Multipeglin.Events;
using Multipeglin.Events.Network.Map;
using Multipeglin.Events.Subscriptions;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using PeglinUI;
using PixelCrushers.DialogueSystem;
using RNG.Scenarios;
using Scenarios;
using Tutorial;
using UnityEngine;
using UnityEngine.EventSystems;
using Worldmap;
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
    private static bool ShouldSuppressClientLogic
    {
        get
        {
            if (MultiplayerPlugin.Services == null) return false;
            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)) return false;
            return mode.IsSpectating;
        }
    }

    private static bool IsHosting
    {
        get
        {
            if (MultiplayerPlugin.Services == null) return false;
            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)) return false;
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
    /// Tracks the relic effect chosen by the client during the post-battle
    /// boss/rare relic selection. Reset when the reward phase ends.
    /// -1 means no relic chosen (skipped or not yet selected).
    /// </summary>
    internal static int ClientChosenPostBattleRelicEffect = -1;
    internal static string ClientChosenPostBattleRelicName;

    // Track fired ball for position diagnostics
    private static UnityEngine.GameObject _firedBallGO;
    private static float _firedBallTimer;
    private static int _firedBallLogCount;

    /// <summary>
    /// The primary ball currently being tracked (the one whose position is streamed
    /// as BallPositionEvent). HostBallRegistry uses this to avoid attaching a
    /// duplicate streamer to the primary ball (which would render twice on the client).
    /// </summary>
    public static UnityEngine.GameObject PrimaryBall => _firedBallGO;

    // =========================================================================
    // DISABLE TUTORIAL IN MULTIPLAYER — both host and client
    // =========================================================================

    /// <summary>
    /// Disable tutorial popups for both host and client in multiplayer.
    /// Tutorials block gameplay and don't make sense in a multiplayer context.
    /// </summary>
    [HarmonyPatch(typeof(TutorialManager), "ShouldPopupTutorial")]
    [HarmonyPrefix]
    public static bool TutorialManager_ShouldPopupTutorial_Prefix(ref bool __result)
    {
        if (MultiplayerPlugin.Services == null) return true;
        if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)) return true;
        if (!mode.IsHosting && !mode.IsSpectating) return true;

        __result = false;
        return false;
    }

    // =========================================================================
    // SKIP CHARACTER SELECT IN MULTIPLAYER — class already chosen in lobby
    // =========================================================================

    /// <summary>
    /// When in a multiplayer session, skip the character select screen entirely.
    /// The host calls PlayButton.MovetoCharacterSelect() which eventually shows
    /// the class select UI. We intercept this to call ConfirmRunConfigAndStartGame()
    /// directly, since the class was already chosen in the lobby.
    /// </summary>
    [HarmonyPatch(typeof(PeglinUI.MainMenu.PlayButton), "SwitchToRunConfigCanvas")]
    [HarmonyPrefix]
    public static bool PlayButton_SwitchToRunConfigCanvas_Prefix(PeglinUI.MainMenu.PlayButton __instance)
    {
        if (MultiplayerPlugin.Services == null) return true;
        if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)) return true;
        if (!mode.IsHosting && !mode.IsSpectating) return true;

        // In multiplayer, skip character select and go straight to game start
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Skipping character select — class chosen in lobby");

        // Set StartingOrbs and StartingRelics from the chosen class's ClassLoadoutData.
        // Normally LoadoutManager.SetupDataForNewGame() does this, but we skip that UI entirely.
        SetStartingLoadoutFromClass(StaticGameData.chosenClass);

        __instance.ConfirmRunConfigAndStartGame();
        return false; // Skip the normal SwitchToRunConfigCanvas
    }

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
            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Called RelicManager.PopulateRelicPools({chosenClass})");
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

    // =========================================================================
    // BLOCK CLIENT GAME LOGIC — client is a dumb renderer
    // =========================================================================

    /// <summary>
    /// Block fast forward input on client — host controls game speed.
    /// The host's speedup state is synced via PlayerStateSnapshot.
    /// </summary>
    [HarmonyPatch(typeof(TimescaleManager), "Update")]
    [HarmonyPrefix]
    public static bool TimescaleManager_Update_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(TimescaleManager), "ManualSpeedupToggle")]
    [HarmonyPrefix]
    public static bool TimescaleManager_ManualSpeedupToggle_Prefix() => !ShouldSuppressClientLogic;

    /// <summary>
    /// Hide the key binding label ("F") on the speedup indicator for client.
    /// Keeps the arrow icon and speed text (e.g., "x2") visible.
    /// </summary>
    [HarmonyPatch(typeof(PeglinUI.SpeedupIndicator), "Start")]
    [HarmonyPostfix]
    public static void SpeedupIndicator_Start_Postfix(PeglinUI.SpeedupIndicator __instance)
    {
        if (!ShouldSuppressClientLogic) return;

        // The SpeedupIndicator Image shows the arrow icon — keep it.
        // Find and hide the key prompt child (the "F" label).
        // The key prompt is typically a child with a text or image showing the keybind.
        foreach (var img in __instance.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            // Skip the main indicator image (the arrow)
            if (img.gameObject == __instance.gameObject) continue;
            // Skip the speed text's parent
            if (img.GetComponentInChildren<TMPro.TextMeshProUGUI>() == __instance.Text) continue;
            // Disable other child images (key prompt icon)
            img.gameObject.SetActive(false);
        }
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
    private static bool _clientDiscardSentThisTurn;

    // Throttled aim update sending from client to host (10 Hz)
    private static float _clientAimSendTimer;
    private const float ClientAimSendInterval = 0.1f;

    // Throttled diagnostic logging for client aiming state
    private static float _clientAimDiagTimer;

    [HarmonyPatch(typeof(BattleController), "Update")]
    [HarmonyPrefix]
    public static bool BattleController_Update_Prefix(BattleController __instance)
    {
        if (!ShouldSuppressClientLogic) return true;

        bool isMyTurn = Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn;
        bool shotSent = ClientShotSentThisTurn;

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
        }
        else
        {
            // Not aiming — clean up client ball and trajectory
            if (_clientBallInitialized)
                CleanupClientAiming();
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

    // Track whether we've initialized the ball for client aiming this turn
    private static bool _clientBallInitialized;

    // The client-created ball GO for the orb visual at spawn point
    private static UnityEngine.GameObject _clientBallGO;

    // Our own trajectory LineRenderer (separate from the ball's TrajectorySimulation)
    private static UnityEngine.GameObject _clientTrajectoryGO;
    private static UnityEngine.LineRenderer _clientTrajectoryLR;

    // Physics parameters read from the ball prefab for trajectory calculation
    private static float _clientFireForce;
    private static float _clientBallMass;
    private static float _clientGravityScale;

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
    private static void CleanupClientAiming()
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
                    try { pm?.PlayerFired(); } catch { }
                }

                var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
                if (bc != null)
                {
                    var field = HarmonyLib.AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
                    if (field?.GetValue(bc) == _clientBallGO)
                        field.SetValue(bc, null);
                }
            }
            catch { }
            UnityEngine.Object.Destroy(_clientBallGO);
            _clientBallGO = null;
        }
        if (_clientTrajectoryGO != null) { UnityEngine.Object.Destroy(_clientTrajectoryGO); _clientTrajectoryGO = null; }
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
    private static int _clientBallRetryCount;
    private const int MaxBallRetries = 30; // ~0.5s at 60fps

    private static void HandleClientAiming(BattleController bc)
    {
        if (!_clientBallInitialized)
        {
            // Rate-limit retries to avoid log spam
            if (_clientBallRetryCount >= MaxBallRetries)
                return;

            CleanupClientAiming();

            // Get spawn position
            var spawnPos = bc.pachinkoBallSpawnLocation;
            if (spawnPos == UnityEngine.Vector2.zero)
            {
                var player = UnityEngine.GameObject.FindGameObjectWithTag("Player");
                if (player != null) spawnPos = (UnityEngine.Vector2)player.transform.position;
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
                        MultiplayerPlugin.Logger?.LogWarning($"[ClientAim] No DeckManager (retry {_clientBallRetryCount})");
                    return;
                }

                var shuffledField = HarmonyLib.AccessTools.Field(typeof(DeckManager), "shuffledDeck");
                var shuffled = shuffledField?.GetValue(dm) as System.Collections.Generic.Stack<UnityEngine.GameObject>;
                UnityEngine.GameObject prefab = null;
                if (shuffled != null && shuffled.Count > 0)
                    prefab = shuffled.Peek();
                if (prefab == null && DeckManager.completeDeck != null && DeckManager.completeDeck.Count > 0)
                    prefab = DeckManager.completeDeck[0];
                if (prefab == null)
                {
                    _clientBallRetryCount++;
                    if (_clientBallRetryCount == 1 || _clientBallRetryCount == MaxBallRetries)
                        MultiplayerPlugin.Logger?.LogWarning($"[ClientAim] No orb prefab (shuffled={shuffled?.Count ?? -1}, complete={DeckManager.completeDeck?.Count ?? -1}, retry {_clientBallRetryCount})");
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
                if (_clientGravityScale < 0) _clientGravityScale = -_clientGravityScale;

                // Disable physics so ball doesn't fall
                if (rb != null) rb.simulated = false;

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
                    try { ball.Arm(); } catch (System.Exception armEx)
                    {
                        MultiplayerPlugin.Logger?.LogWarning($"[ClientAim] Arm() failed (non-fatal): {armEx.Message}");
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

    /// <summary>
    /// Send the client's current aim direction to the host so it can render
    /// the aim line. Uses IMessageSender.Send() (client→host network path).
    /// </summary>
    private static void SendClientAimUpdate()
    {
        if (_clientBallGO == null) return;
        var ball = _clientBallGO.GetComponent<PachinkoBall>();
        if (ball == null) return;

        var aimVec = ball.aimVector;
        if (aimVec == UnityEngine.Vector2.zero) return;

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
    private static void DrawClientTrajectory(UnityEngine.Vector3 start, UnityEngine.Vector2 aimDir)
    {
        const int segments = 20;
        const float segmentScale = 1f;

        var positions = new UnityEngine.Vector3[segments];
        positions[0] = start;

        // Initial velocity: matches TrajectorySimulation line 61
        // Original uses Time.deltaTime which varies per frame; we use fixedDeltaTime for stability
        UnityEngine.Vector3 vel = (UnityEngine.Vector3)(aimDir * (_clientFireForce * UnityEngine.Time.fixedDeltaTime) / _clientBallMass);

        for (int i = 1; i < segments; i++)
        {
            float dt = vel.sqrMagnitude > 0f ? segmentScale / vel.magnitude : 0f;
            vel += UnityEngine.Physics.gravity * (_clientGravityScale * dt);
            positions[i] = positions[i - 1] + vel * dt;
        }

        _clientTrajectoryLR.positionCount = segments;
        _clientTrajectoryLR.SetPositions(positions);
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
        if (!IsHosting) return;
        if (!UI.LobbyUI.GameStartReceived) return;

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

        // Only process when BattleController is in AWAITING_SHOT
        if (BattleController.CurrentBattleState != BattleController.BattleState.AWAITING_SHOT) return;

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
                bool wasInactive = ballGO != null && !ballGO.activeInHierarchy;
                if (wasInactive) ballGO.SetActive(true);

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
                    int skipCount = (int)skipField.GetValue(bc);
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
        if (pending == null) return;

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
            if (bc == null) return;

            var activeBallField = HarmonyLib.AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
            var activeBallGO = activeBallField?.GetValue(bc) as UnityEngine.GameObject;
            if (activeBallGO == null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] PendingShot from {pending.PlayerName} but _activePachinkoBall is null");
                return;
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
                UnityEngine.Vector3 targetScale = activeBallGO.transform.localScale;
                bool hadTween = false;
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
                    targetScale = new UnityEngine.Vector3(0.32f, 0.32f, 0.32f);
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
            catch { }

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
        if (!UI.LobbyUI.GameStartReceived) return;

        var bc = UnityEngine.Object.FindObjectOfType<BattleController>();
        if (bc == null) return;

        var activeBallField = HarmonyLib.AccessTools.Field(typeof(BattleController), "_activePachinkoBall");
        var ballGO = activeBallField?.GetValue(bc) as UnityEngine.GameObject;
        if (ballGO == null) return;

        // On the host during a client's turn, hide the ball so the host
        // can't see the aimer or interact with it.
        if (IsHosting)
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<GameState.TurnManager>(out var tm) == true
                && tm.CurrentPlayerSlot > 0)
            {
                if (ballGO.activeInHierarchy)
                    ballGO.SetActive(false);
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
    /// Set to true by BattleController_Update_Postfix just before calling
    /// AttemptOrbDiscard() to execute a client's pending discard request. This
    /// prevents AttemptOrbDiscard_Prefix from blocking the programmatic discard.
    /// </summary>
    private static bool _executingPendingDiscard;

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
        if (!UI.LobbyUI.GameStartReceived) return true;

        // Block on client — discards are handled via OrbDiscardRequestEvent to host
        if (ShouldSuppressClientLogic) return false;

        if (!IsHosting) return true;
        if (_executingPendingDiscard) return true; // bypass for programmatic discard

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.TurnManager>(out var tm) == true
            && tm.CurrentPlayerSlot > 0) // client's turn
        {
            return false; // block manual discard from host player
        }
        return true;
    }

    [HarmonyPatch(typeof(SaveManager), "SaveRun")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveRun_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(SaveManager), "SaveBase")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveBase_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(GameInit), "Start")]
    [HarmonyPrefix]
    public static bool GameInit_Start_Prefix()
    {
        // In coop mode (lobby game start), allow GameInit so each player gets their own deck/relics
        if (UI.LobbyUI.GameStartReceived)
            return true;
        return !ShouldSuppressClientLogic;
    }

    /// <summary>
    /// After GameInit.Start() completes in coop mode, initialize per-player state
    /// in CoopStateManager, capture the host's initial state, and skip the relic
    /// selection screen by calling LoadMapScene() directly.
    /// </summary>
    [HarmonyPatch(typeof(GameInit), "Start")]
    [HarmonyPostfix]
    public static void GameInit_Start_Postfix(GameInit __instance)
    {
        // Only run when hosting or in coop mode
        if (!UI.LobbyUI.GameStartReceived) return;

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) != true) return;

        // Clear stale reward/relic-selection state from any previous run. Without this,
        // flags like HostHasChosenRelic and the various *PhaseActive bools persist from
        // the prior game and prevent the starting relic UI from advancing on the second run.
        Events.Handlers.Coop.CoopRewardState.Reset();

        var gameStartEvent = UI.LobbyUI.LatestGameStartEvent;
        if (gameStartEvent?.FinalPlayers != null)
        {
            // Initialize players only on the first run. On subsequent GameInit.Start()
            // calls (new runs), re-initialize to reset accumulated state.
            // Check if players already exist and match the expected count.
            bool needsInit = coopState.TotalPlayerCount != gameStartEvent.FinalPlayers.Count;
            if (!needsInit)
            {
                foreach (var player in gameStartEvent.FinalPlayers)
                {
                    var existing = coopState.GetPlayerState(player.SlotIndex);
                    if (existing == null || !existing.IsInitialized)
                    { needsInit = true; break; }
                }
            }

            if (needsInit)
            {
                foreach (var player in gameStartEvent.FinalPlayers)
                {
                    coopState.InitializePlayer(player.SlotIndex, player.ChosenClass, player.PlayerName);
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Initialized coop player: slot={player.SlotIndex}, name={player.PlayerName}, class={player.ChosenClass}");
                }
            }
            else
            {
                MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Players already initialized ({coopState.TotalPlayerCount}), re-capturing state");
            }

            // Capture/re-capture host's state (slot 0) after GameInit has set up deck/relics/health.
            // This runs on every GameInit.Start() so the host's deck is always current.
            coopState.CaptureInitialState(0);
            coopState.ActivePlayerSlot = 0;

            // CaptureInitialState reads health from PlayerHealthController, which may not
            // exist on the PostMainMenu scene. Read directly from GameInit's FloatVariable
            // ScriptableObject references which ARE set after Start() completes.
            var hostState = coopState.GetPlayerState(0);
            if (hostState != null && hostState.MaxHealth <= 0)
            {
                try
                {
                    float hp = __instance.playerHealth?.Value ?? 0;
                    float maxHp = __instance.maxPlayerHealth?.Value ?? 0;
                    if (maxHp > 0)
                    {
                        hostState.CurrentHealth = hp;
                        hostState.MaxHealth = maxHp;
                        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Health from GameInit FloatVars: hp={hp}/{maxHp}");
                    }
                }
                catch (System.Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Failed to read health from GameInit: {ex.Message}");
                }
            }

            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Coop: captured host initial state, ActivePlayerSlot=0, " +
                $"{gameStartEvent.FinalPlayers.Count} players, hp={hostState?.CurrentHealth}/{hostState?.MaxHealth}, " +
                $"deck={hostState?.CompleteDeck.Count}");

            // Build starting state for non-host players from ClassLoadoutData.
            // The host's singletons contain the host's data, so we can't use
            // CaptureInitialState for other slots. Instead, directly populate
            // each non-host player's CoopPlayerState from their class loadout.
            // Only on first initialization — skip if player already has state.
            foreach (var player in gameStartEvent.FinalPlayers)
            {
                if (player.IsHost) continue;

                var playerState = coopState.GetPlayerState(player.SlotIndex);
                if (playerState == null) continue;

                // Skip if player already has initialized state (re-capture, not re-init)
                if (playerState.IsInitialized && playerState.CompleteDeck.Count > 0)
                {
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Slot {player.SlotIndex} already initialized with {playerState.CompleteDeck.Count} orbs, skipping re-init");
                    continue;
                }

                // All players start with the same max HP as the host
                float maxHp = hostState?.MaxHealth ?? (__instance.maxPlayerHealth?.Value ?? 0);
                playerState.CurrentHealth = maxHp; // Full health at start
                playerState.MaxHealth = maxHp;

                // Build starting deck from ClassLoadoutData
                var targetClass = (Peglin.ClassSystem.Class)player.ChosenClass;
                var classLoadouts = StaticGameData.classLoadouts;
                Peglin.ClassSystem.ClassLoadoutData loadout = null;
                if (classLoadouts != null)
                {
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Slot {player.SlotIndex}: searching {classLoadouts.Length} classLoadouts for class {targetClass} (int={player.ChosenClass})");
                    foreach (var pair in classLoadouts)
                    {
                        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches]   classLoadout: {pair.Class} orbs={pair.Loadout?.StartingOrbs?.Count ?? 0}");
                        if (pair.Class == targetClass)
                        { loadout = pair.Loadout; break; }
                    }
                }
                else
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Slot {player.SlotIndex}: StaticGameData.classLoadouts is NULL — cannot look up {targetClass}");
                }

                if (loadout?.StartingOrbs != null)
                {
                    playerState.CompleteDeck.Clear();
                    foreach (var orb in loadout.StartingOrbs)
                    {
                        if (orb == null) continue;
                        playerState.CompleteDeck.Add(new GameState.SerializedOrb
                        {
                            PrefabName = orb.name,
                            Level = 0,
                        });
                    }
                    var deckNames = string.Join(", ", playerState.CompleteDeck.Select(o => o.PrefabName));
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Slot {player.SlotIndex}: built deck from {targetClass} ClassLoadoutData: [{deckNames}]");
                }
                else
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Slot {player.SlotIndex}: ClassLoadoutData for {targetClass} has NO StartingOrbs (loadout={loadout != null})");
                }

                if (loadout?.StartingRelics != null)
                {
                    playerState.OwnedRelics.Clear();
                    foreach (var relic in loadout.StartingRelics)
                    {
                        if (relic == null) continue;
                        playerState.OwnedRelics.Add(new GameState.SerializedRelic
                        {
                            Effect = (int)relic.effect,
                            LocKey = relic.locKey ?? "",
                            Rarity = (int)relic.globalRarity,
                        });
                    }
                }

                playerState.IsInitialized = true;
                MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Built slot {player.SlotIndex} state from ClassLoadoutData: " +
                    $"hp={playerState.CurrentHealth}/{playerState.MaxHealth}, deck={playerState.CompleteDeck.Count}, relics={playerState.OwnedRelics.Count}");

                // On the CLIENT: if this is our slot, add the starting class relics
                // to the local RelicManager so they show in the UI. The host side
                // stores them in CoopPlayerState and loads them via LoadRelicState
                // during battle, but the client needs them in its own RelicManager too.
                if (!IsHosting && loadout?.StartingRelics != null)
                {
                    try
                    {
                        var clientRelicMgrs = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                        if (clientRelicMgrs != null && clientRelicMgrs.Length > 0)
                        {
                            foreach (var relic in loadout.StartingRelics)
                            {
                                if (relic == null) continue;
                                try
                                {
                                    AllowRelicSync = true;
                                    clientRelicMgrs[0].AddRelic(relic);
                                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Client: added starting class relic {relic.effect} ({relic.locKey})");
                                }
                                catch { }
                                finally { AllowRelicSync = false; }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        // Coop relic selection: both host and clients choose a starting relic.
        // The host sees the game's native relic canvas; clients see CoopRewardUI.
        // LoadMapScene is blocked until all players have chosen.
        if (IsHosting)
        {
            // Initialize relic selection tracking state
            int nonHostCount = 0;
            if (gameStartEvent?.FinalPlayers != null)
            {
                foreach (var p in gameStartEvent.FinalPlayers)
                    if (!p.IsHost) nonHostCount++;
            }
            Events.Handlers.Coop.CoopRewardState.HostRelicSelectionActive = true;
            Events.Handlers.Coop.CoopRewardState.HostHasChosenRelic = false;
            Events.Handlers.Coop.CoopRewardState.TotalClientsExpected = nonHostCount;
            Events.Handlers.Coop.CoopRewardState.ClientRelicChoicesReceived.Clear();
            Events.Handlers.Coop.CoopRewardState.PendingGameInitInstance = __instance;

            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Coop host: entering relic selection phase, waiting for {nonHostCount} client(s)");

            // Generate starting relic choices for each non-host player and send
            if (services.TryResolve<IGameEventRegistry>(out var registry) && gameStartEvent?.FinalPlayers != null)
            {
                var relicMgrs = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                if (relicMgrs != null && relicMgrs.Length > 0)
                {
                    var rm = relicMgrs[0];
                    foreach (var player in gameStartEvent.FinalPlayers)
                    {
                        if (player.IsHost) continue;

                        var choices = new System.Collections.Generic.List<GameState.Snapshots.RelicEntry>();
                        try
                        {
                            var relics = rm.GetMultipleRelicsOffOfQueue(3, Relics.RelicRarity.COMMON);
                            foreach (var relic in relics)
                            {
                                string displayName = "";
                                try
                                {
                                    displayName = LocalizationManager.GetTranslation(relic.nameKey);
                                    if (string.IsNullOrEmpty(displayName))
                                        displayName = relic.englishDisplayName ?? relic.locKey ?? "Unknown";
                                }
                                catch { displayName = relic.englishDisplayName ?? relic.locKey ?? "Unknown"; }

                                string description = "";
                                try
                                {
                                    description = LocalizationManager.GetTranslation(relic.descKey);
                                    if (string.IsNullOrEmpty(description))
                                        description = relic.locKey ?? "";
                                }
                                catch { description = relic.locKey ?? ""; }

                                choices.Add(new GameState.Snapshots.RelicEntry
                                {
                                    Effect = (int)relic.effect,
                                    EffectName = displayName,
                                    LocKey = description,
                                    Rarity = (int)relic.globalRarity,
                                    IsEnabled = true,
                                });
                            }
                        }
                        catch (Exception ex2)
                        {
                            MultiplayerPlugin.Logger?.LogWarning($"[GameInit] Failed to generate relic choices for slot {player.SlotIndex}: {ex2.Message}");
                        }

                        if (choices.Count > 0)
                        {
                            registry.Dispatch(new Events.Network.Coop.RelicChoicesEvent
                            {
                                TargetSlotIndex = player.SlotIndex,
                                Choices = choices,
                            });
                            MultiplayerPlugin.Logger?.LogInfo($"[GameInit] Sent {choices.Count} relic choices to slot {player.SlotIndex}");
                        }
                    }

                    // Also generate relic choices for the host and display via CoopRewardUI
                    try
                    {
                        var hostChoices = new System.Collections.Generic.List<GameState.Snapshots.RelicEntry>();
                        var relics2 = rm.GetMultipleRelicsOffOfQueue(3, Relics.RelicRarity.COMMON);
                        foreach (var relic in relics2)
                        {
                            string displayName2 = "";
                            try
                            {
                                displayName2 = LocalizationManager.GetTranslation(relic.nameKey);
                                if (string.IsNullOrEmpty(displayName2))
                                    displayName2 = relic.englishDisplayName ?? relic.locKey ?? "Unknown";
                            }
                            catch { displayName2 = relic.englishDisplayName ?? relic.locKey ?? "Unknown"; }

                            string description2 = "";
                            try
                            {
                                description2 = LocalizationManager.GetTranslation(relic.descKey);
                                if (string.IsNullOrEmpty(description2))
                                    description2 = relic.locKey ?? "";
                            }
                            catch { description2 = relic.locKey ?? ""; }

                            hostChoices.Add(new GameState.Snapshots.RelicEntry
                            {
                                Effect = (int)relic.effect,
                                EffectName = displayName2,
                                LocKey = description2,
                                Rarity = (int)relic.globalRarity,
                                IsEnabled = true,
                            });
                        }
                        Events.Handlers.Coop.CoopRewardState.PendingRelicChoices = new Events.Network.Coop.RelicChoicesEvent
                        {
                            TargetSlotIndex = 0,
                            Choices = hostChoices,
                        };
                        MultiplayerPlugin.Logger?.LogInfo($"[GameInit] Generated {hostChoices.Count} relic choices for host (slot 0)");
                    }
                    catch (Exception ex4)
                    {
                        MultiplayerPlugin.Logger?.LogWarning($"[GameInit] Failed to generate host relic choices: {ex4.Message}");
                    }
                }
            }

            // Hide the native relic canvas — host uses CoopRewardUI like clients
            try
            {
                var canvasField = typeof(GameInit).GetField("_chooseRelicCanvas",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var canvasObj = canvasField?.GetValue(__instance) as GameObject;
                if (canvasObj != null)
                {
                    canvasObj.SetActive(false);
                    MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Coop host: hid native relic canvas, using CoopRewardUI");
                }
            }
            catch (Exception ex5)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Failed to hide host relic canvas: {ex5.Message}");
            }
        }
        else if (!IsHosting)
        {
            // Client: hide the game's native relic canvas — the client uses CoopRewardUI instead
            try
            {
                var canvasField = typeof(GameInit).GetField("_chooseRelicCanvas",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var canvasObj = canvasField?.GetValue(__instance) as GameObject;
                if (canvasObj != null)
                {
                    canvasObj.SetActive(false);
                    MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Coop client: hid native relic canvas, using CoopRewardUI");
                }
            }
            catch (Exception ex3)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Failed to hide relic canvas: {ex3.Message}");
            }
        }
    }

    // =========================================================================
    // GATE LoadMapScene ON ALL RELIC CHOICES — host waits for clients
    // =========================================================================

    /// <summary>
    /// Intercept GameInit.LoadMapScene during coop relic selection.
    /// When the host chooses their relic, the game calls LoadMapScene via the
    /// SkipRelic tween callback. We block it until all clients have also chosen.
    /// </summary>
    [HarmonyPatch(typeof(GameInit), "LoadMapScene")]
    [HarmonyPrefix]
    public static bool GameInit_LoadMapScene_Prefix()
    {
        // Only intercept during coop relic selection
        if (!Events.Handlers.Coop.CoopRewardState.HostRelicSelectionActive) return true;
        if (!IsHosting) return true;

        // Host has chosen their relic -- mark it
        Events.Handlers.Coop.CoopRewardState.HostHasChosenRelic = true;

        // Check if all clients have also chosen
        if (Events.Handlers.Coop.CoopRewardState.AllClientRelicChoicesReceived)
        {
            // Everyone done -- allow LoadMapScene to proceed
            Events.Handlers.Coop.CoopRewardState.HostRelicSelectionActive = false;
            Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = true;
            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = false;
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] All relic choices received -- proceeding to map");

            // Dispatch AllChoicesCompleteEvent
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IGameEventRegistry>(out var registry) == true)
            {
                registry.Dispatch(new Events.Network.Coop.AllChoicesCompleteEvent { Phase = "starting_relic" });
            }
            return true; // Allow LoadMapScene
        }

        // Not all clients have chosen yet -- block and show waiting
        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Host chose relic, waiting for " +
            $"{Events.Handlers.Coop.CoopRewardState.TotalClientsExpected - Events.Handlers.Coop.CoopRewardState.ClientRelicChoicesReceived.Count} more client(s)");
        Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
        return false; // Block LoadMapScene until all done
    }

    // =========================================================================
    // BLOCK CLIENT SCENE LOADS — only our sync handlers may load scenes
    // =========================================================================

    /// <summary>
    /// Block ALL scene loads on the client except those explicitly initiated by our
    /// sync system (NodeActivatedClientHandler, MapStateApplier). This prevents the
    /// game's own MapController/node flow from triggering a second Battle load after
    /// we've already loaded the correct scene.
    /// </summary>
    [HarmonyPatch(typeof(PeglinSceneLoader), nameof(PeglinSceneLoader.LoadScene),
        new[] { typeof(PeglinSceneLoader.Scene), typeof(UnityEngine.SceneManagement.LoadSceneMode), typeof(bool), typeof(float) })]
    [HarmonyPrefix]
    public static bool PeglinSceneLoader_LoadScene_Prefix(PeglinSceneLoader.Scene scene)
    {
        if (!ShouldSuppressClientLogic) return true;

        if (AllowNextSceneLoad)
        {
            AllowNextSceneLoad = false;
            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] ALLOWING scene load: {scene} (sync-initiated)");
            return true;
        }

        MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] BLOCKED scene load: {scene} (not sync-initiated)");
        return false;
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
        if (!ShouldSuppressClientLogic) return;

        // 1. Destroy pre-instanced pegs
        var preData = StaticGameData.preInstancedPegboardData;
        if (preData != null)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Destroying preInstancedPegboardData on client " +
                $"(pegboard={preData.pegboardData?.name}, root={preData.rootGameObject?.name})");
            if (preData.rootGameObject != null)
                UnityEngine.Object.DestroyImmediate(preData.rootGameObject);
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
        if (__exception == null) return null;
        if (!ShouldSuppressClientLogic) return __exception;

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
                    int loaded = 0;
                    foreach (var spawn in battle.starterSpawns)
                    {
                        try
                        {
                            if (spawn?.spawnData?.enemyAssetReference == null) continue;
                            var key = spawn.spawnData.enemyAssetReference.RuntimeKey.ToString();
                            if (!cache.ContainsKey(key))
                            {
                                var go = spawn.spawnData.enemyAssetReference.LoadAssetAsync<GameObject>().WaitForCompletion();
                                if (go != null) { cache[key] = go; loaded++; }
                            }
                        }
                        catch { }
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

    // =========================================================================
    // BLOCK CLIENT MAP GENERATION — host controls map layout
    // =========================================================================

    /// <summary>
    /// Block map node type generation on client. MapController.Start calls
    /// CreateMapDataLists which assigns random room types to nodes. On the
    /// client, the host sends the correct node types via MapStateSnapshot.
    /// Without this block, the client generates its own map with wrong types.
    /// </summary>
    /// <summary>
    /// Let CreateMapDataLists run on client — it just initializes empty lists/queues
    /// for battle and scenario selection. Blocking it causes Start to crash with NRE
    /// when subsequent code references the missing lists. The lists aren't used for
    /// anything on the client since node types come from the host.
    /// </summary>
    [HarmonyPatch(typeof(Map.MapController), "CreateMapDataLists")]
    [HarmonyPostfix]
    public static void MapController_CreateMapDataLists_Postfix()
    {
        if (!ShouldSuppressClientLogic) return;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] CreateMapDataLists ran on client (lists unused, prevents NRE)");
    }

    /// <summary>Block post-processing of map on client (relic-based node changes).</summary>
    [HarmonyPatch(typeof(Map.MapController), "PostProcessMap")]
    [HarmonyPrefix]
    public static bool MapController_PostProcessMap_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    /// <summary>Block seeding map contents on client.</summary>
    [HarmonyPatch(typeof(Map.MapController), "SeedMapContents")]
    [HarmonyPrefix]
    public static bool MapController_SeedMapContents_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    /// <summary>Block save requests on client.</summary>
    [HarmonyPatch(typeof(SaveManager), "RequestSave")]
    [HarmonyPrefix]
    public static bool SaveManager_RequestSave_Prefix() => !ShouldSuppressClientLogic;

    /// <summary>
    /// Block map-initiated scene loading on client. The map controller's own
    /// LoadSceneFromMapData would load scenes from the client's (wrong) map data.
    /// Our NodeActivatedClientHandler handles scene transitions with the correct data.
    /// Also clears the fade curtain — the game starts a fade-to-black before loading,
    /// and blocking the load leaves the screen black permanently.
    /// </summary>
    [HarmonyPatch(typeof(Map.MapController), "LoadSceneFromMapData")]
    [HarmonyPrefix]
    public static bool MapController_LoadSceneFromMapData_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked LoadSceneFromMapData — host will send transitions");

        // Clear fade curtain — the game started a fade-to-black before we blocked the load
        try
        {
            var curtain = UnityEngine.Object.FindObjectOfType<PeglinUI.FadeCurtain>();
            if (curtain != null)
            {
                curtain.FadeOut();
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Block ResolveNode on the client. MapController is DontDestroyOnLoad, so its
    /// NodeSelected coroutine (started by walk completion) leaks across the scene
    /// transition into TextScenario/Shop/Treasure/etc. Inside that coroutine,
    /// DoNodeSelectionFadeOut finds the "Curtain"-tagged Image in the NEW scene
    /// (e.g., the DialogueSystemScenario curtain) and fades it to fully black —
    /// permanently hiding the dialogue UI.
    ///
    /// Scene transitions on the client are already handled by NodeActivatedClientHandler,
    /// so we never need the client's own MapController.ResolveNode flow.
    /// </summary>
    [HarmonyPatch(typeof(Map.MapController), "ResolveNode")]
    [HarmonyPrefix]
    public static bool MapController_ResolveNode_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked MapController.ResolveNode (client — scene handled by NodeActivatedClientHandler)");
        return false;
    }

    // =========================================================================
    // BLOCK CLIENT AUTO-GENERATION — host controls all content
    // =========================================================================

    /// <summary>
    /// Block enemy spawning on client. BattleController.Awake still calls
    /// EnemyManager.Initialize (which sets up slots) but AddStarterEnemies
    /// is blocked. The host sends enemy data and the applier creates them.
    /// LoadEnemyAssets still runs so the prefab cache is populated.
    /// </summary>
    [HarmonyPatch(typeof(EnemyManager), "AddStarterEnemies")]
    [HarmonyPrefix]
    public static bool EnemyManager_AddStarterEnemies_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked AddStarterEnemies — host will send enemies");
        return false;
    }

    /// <summary>
    /// Block upcoming enemy preview generation on client. The host sends the
    /// actual upcoming enemy list and the applier rebuilds the UI from it.
    /// </summary>
    [HarmonyPatch(typeof(Battle.EnemyInfoManager), "Initialize")]
    [HarmonyPrefix]
    public static bool EnemyInfoManager_Initialize_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked EnemyInfoManager.Initialize — host will send upcoming enemies");
        return false;
    }

    /// <summary>
    /// Block status effect application on client enemies. The host sends the correct
    /// status effects via heartbeat and the applier sets them directly. Without this,
    /// the client's own attack resolution keeps stacking effects every frame.
    /// The applier sets AllowStatusEffectSync=true while it's applying host effects.
    /// </summary>
    internal static bool AllowStatusEffectSync;

    [HarmonyPatch(typeof(Battle.Enemies.Enemy), "ApplyStatusEffect")]
    [HarmonyPrefix]
    public static bool Enemy_ApplyStatusEffect_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return AllowStatusEffectSync; // only allow when the applier is syncing
    }

    /// <summary>
    /// Block special peg type shuffling on client. The pegboard layout loads
    /// with all pegs as REGULAR. The host sends the correct peg types and
    /// the applier sets them.
    ///
    /// On the host, log the caller so we can diagnose excessive shuffle
    /// frequency (see bug report: "shuffle fucks up everything").
    /// </summary>
    [HarmonyPatch(typeof(PegManager), "ShuffleSpecialPegs")]
    [HarmonyPrefix]
    public static bool PegManager_ShuffleSpecialPegs_Prefix(bool forceRefresh)
    {
        if (ShouldSuppressClientLogic)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked ShuffleSpecialPegs — host will send peg types");
            return false;
        }
        if (IsHosting)
            MultiplayerPlugin.Logger?.LogInfo(
                $"[PegShuffleHost] ShuffleSpecialPegs(forceRefresh={forceRefresh}) caller={DescribeShuffleCaller()}");
        return true;
    }

    /// <summary>
    /// Block individual special peg creation on client.
    /// Covers ShuffleCritPegs, CreateRefreshPegs, and direct CreateSpecialPegs calls.
    /// </summary>
    [HarmonyPatch(typeof(PegManager), "CreateSpecialPegs")]
    [HarmonyPrefix]
    public static bool PegManager_CreateSpecialPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    /// <summary>
    /// Block crit peg shuffling on client; on host, log the caller.
    /// </summary>
    [HarmonyPatch(typeof(PegManager), "ShuffleCritPegs")]
    [HarmonyPrefix]
    public static bool PegManager_ShuffleCritPegs_Prefix()
    {
        if (ShouldSuppressClientLogic) return false;
        if (IsHosting)
            MultiplayerPlugin.Logger?.LogInfo(
                $"[PegShuffleHost] ShuffleCritPegs caller={DescribeShuffleCaller()}");
        return true;
    }

    /// <summary>
    /// Return a short string identifying the caller of a shuffle method.
    /// Walks up the stack past Harmony wrappers and the shuffle method itself,
    /// returning up to 3 user frames in "Type.Method > Type.Method" order.
    /// </summary>
    private static string DescribeShuffleCaller()
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(2, false);
            var picked = new System.Collections.Generic.List<string>();
            for (int i = 0; i < trace.FrameCount && picked.Count < 3; i++)
            {
                var m = trace.GetFrame(i)?.GetMethod();
                if (m == null) continue;
                var t = m.DeclaringType?.FullName ?? "?";
                // Skip Harmony-generated wrappers and this patch class
                if (t.StartsWith("HarmonyLib") || t.StartsWith("System.") ||
                    t.Contains("DMD<") || t.Contains("MultiplayerClientPatches"))
                    continue;
                picked.Add($"{m.DeclaringType?.Name ?? "?"}.{m.Name}");
            }
            return picked.Count == 0 ? "<unknown>" : string.Join(" > ", picked);
        }
        catch { return "<stacktrace-failed>"; }
    }

    /// <summary>Block refresh peg creation on client.</summary>
    [HarmonyPatch(typeof(PegManager), "CreateRefreshPegs")]
    [HarmonyPrefix]
    public static bool PegManager_CreateRefreshPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    /// <summary>
    /// HOST: when a LongPeg's delayed-death timer fires and the peg switches to
    /// its cleared material, immediately fade it out like the client does. The
    /// native game only fades LongPegs at end-of-battle via RemoveClearedPegs,
    /// so without this hook the host sees popped rectangular pegs sit on the
    /// board greyed-out while the client has already faded them to alpha=0.
    /// </summary>
    [HarmonyPatch(typeof(LongPeg), "SetActiveStatus")]
    [HarmonyPostfix]
    public static void LongPeg_SetActiveStatus_Postfix(LongPeg __instance, bool active)
    {
        if (active) return;
        if (!IsHosting) return;
        try
        {
            var clearedField = HarmonyLib.AccessTools.Field(typeof(global::Peg), "_cleared");
            bool isCleared = (bool)(clearedField?.GetValue(__instance) ?? false);
            if (isCleared) __instance.RemoveIfCleared();
        }
        catch { }
    }

    // RegularPeg_PopPeg_Postfix (removed): previously called RemoveIfCleared() on host
    // pegs so they'd fade to invisible, but the DOFade tween it starts has an onComplete
    // Disable() callback that Reset() does NOT kill. When a refresh peg fires during the
    // same shot, pegs popped within the last second get deactivated 1s later despite
    // Reset()'s SetActive(true) call — breaking the refresh. The client keeps popped
    // pegs at scale 0.3 (no fade), so the host doing the same is fine and consistent.

    /// <summary>Block failsafe refresh peg creation on client.</summary>
    [HarmonyPatch(typeof(PegManager), "FailSafeCreateRefreshPegs")]
    [HarmonyPrefix]
    public static bool PegManager_FailSafeCreateRefreshPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    /// <summary>Block peg reset on client — host sync handles peg state.</summary>
    [HarmonyPatch(typeof(PegManager), "ResetPegs")]
    [HarmonyPrefix]
    public static bool PegManager_ResetPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    /// <summary>Block shield peg creation on client — host sync handles shield state.</summary>
    [HarmonyPatch(typeof(PegManager), "ApplyShieldToRegularPegs")]
    [HarmonyPrefix]
    public static bool PegManager_ApplyShieldToRegularPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    /// <summary>
    /// Block RandomPegField's per-turn peg repositioning on client.
    /// When moveEveryTurn is true, RandomPegField.TurnComplete re-randomizes all
    /// peg positions using client-side RNG — causing layout divergence every turn.
    /// The host's periodic sync will send correct positions.
    /// </summary>
    [HarmonyPatch(typeof(Battle.PegBehaviour.RandomPegField), "TurnComplete")]
    [HarmonyPrefix]
    public static bool RandomPegField_TurnComplete_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    /// <summary>
    /// Block DrawBall on client — it NREs on subsequent calls because
    /// BattleController's state machine isn't advancing (Update blocked).
    /// BallUsedClientHandler manually handles deck pop and UI animation.
    /// In coop mode, allow deck operations during the client's own turn.
    /// </summary>
    [HarmonyPatch(typeof(DeckManager), "DrawBall")]
    [HarmonyPrefix]
    public static bool DeckManager_DrawBall_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        // In coop, allow deck operations during client's turn
        if (UI.LobbyUI.GameStartReceived && Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn) return true;
        return false;
    }

    /// <summary>
    /// Block the battle deck reshuffle on client — prevents reload animation spam.
    /// The initial ShuffleCompleteDeck (at battle start) is allowed for UI setup.
    /// ShuffleBattleDeck fires during reload (deck empty) and triggers the plunger
    /// animation loop. The host sends the correct deck order via SyncDeck.
    /// </summary>
    [HarmonyPatch(typeof(DeckManager), "ShuffleBattleDeck")]
    [HarmonyPrefix]
    public static bool DeckManager_ShuffleBattleDeck_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        // In coop, allow deck operations during client's turn
        if (UI.LobbyUI.GameStartReceived && Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn) return true;
        return false;
    }

    /// <summary>Block board field reset on client — prevents re-shuffling pegs.
    /// In coop, allow during client's turn so board refreshes work.</summary>
    [HarmonyPatch(typeof(BattleController), "ResetField")]
    [HarmonyPrefix]
    public static bool BattleController_ResetField_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        // In coop, allow field reset during client's turn
        if (UI.LobbyUI.GameStartReceived && Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn) return true;
        return false;
    }

    /// <summary>
    /// HOST: after Start generates node types, immediately sync the map.
    /// The initial SyncAll fires on scene load BEFORE Start runs, so it captures
    /// NONE types. This postfix sends the real types as soon as they're ready.
    ///
    /// CLIENT: Start runs normally for visual setup (camera pan, intro fade,
    /// character walk). Sub-method blocks (GenerateRoomType, PostProcessMap,
    /// SeedMapContents) prevent wrong state. The Finalizer re-applies correct
    /// node types from _latestMap after Start finishes.
    /// </summary>
    [HarmonyPatch(typeof(Map.MapController), "Start")]
    [HarmonyFinalizer]
    public static Exception MapController_Start_Finalizer(Exception __exception, Map.MapController __instance)
    {
        // HOST: send fresh map sync with real node types
        if (IsHosting)
        {
            if (__exception != null)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Host MapController.Start threw ({__exception.GetType().Name}): {__exception.Message} — recovering intro chain");
                // Start threw before IntroFade could kick off the DOTween chain.
                // Without recovery, the map stays frozen at the boss row (softlock).
                // Directly jump to IntroCameraPan so the pan/walk/activate chain runs.
                try
                {
                    AccessTools.Method(typeof(Map.MapController), "IntroCameraPan")?.Invoke(__instance, null);
                    MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Recovered: invoked IntroCameraPan after Start exception");
                }
                catch (Exception recoverEx)
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Recovery IntroCameraPan failed: {recoverEx.Message}");
                }
            }
            try
            {
                if (MultiplayerPlugin.Services?.TryResolve<GameState.IGameStateSyncService>(out var sync) == true)
                {
                    sync.SyncMap();
                    MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Host MapController.Start done — sent immediate map sync with node types");
                }
            }
            catch { }
            // Swallow Start exceptions on host so Unity doesn't mark the MC broken.
            return null;
        }

        if (!ShouldSuppressClientLogic) return __exception;

        // CLIENT: re-apply host node types (Start set them to NONE via blocked GenerateRoomType)
        MapControllerStartCompleted = true;

        if (__exception != null)
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] MapController.Start threw on client (swallowed): {__exception.Message}");

        try
        {
            if (MultiplayerPlugin.Services?.TryResolve<GameState.GameStateApplyService>(out var applySvc) == true)
                applySvc.ReapplyLastMapState();
        }
        catch { }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] MapController.Start finished on client — re-applied host node types");
        return null; // Swallow exceptions on client
    }

    // =========================================================================
    // MAP INTRO CHAIN DEFENSIVE PATCHES — keep host progressing through the
    // IntroFade → IntroCameraPan → PrePanWait → PostFadeInit → StartGoblinWalk
    // → WalkFinished → ActivateNode DOTween callback chain. If any stage
    // throws (e.g. a Map scene lacks the "Curtain"-tagged GameObject so
    // IntroFade NREs), the chain dies silently and the host softlocks at
    // the bottom of the map. These patches log each stage and recover
    // when the chain stalls.
    // =========================================================================

    /// <summary>
    /// MapController.IntroFade calls GameObject.FindGameObjectWithTag("Curtain").GetComponent&lt;Image&gt;()
    /// with no null check on the tag lookup — so any Map scene missing the
    /// tagged GameObject throws NRE and the onComplete-driven intro chain
    /// never fires (camera pan, goblin walk, node activate all skipped).
    /// On host, short-circuit to IntroCameraPan when the Curtain is missing.
    /// </summary>
    [HarmonyPatch(typeof(Map.MapController), "IntroFade")]
    [HarmonyPrefix]
    public static bool MapController_IntroFade_Prefix(Map.MapController __instance)
    {
        if (!IsHosting) return true;
        try
        {
            var curtainGO = GameObject.FindGameObjectWithTag("Curtain");
            if (curtainGO == null)
            {
                MultiplayerPlugin.Logger?.LogWarning("[ClientPatches] IntroFade: no 'Curtain' tagged GO in scene — skipping DOFade, calling IntroCameraPan directly");

                // Mirror IntroFade's player-position step so the intro starts
                // at _previousNode (if any) even though we're skipping the fade.
                try
                {
                    var prevNode = AccessTools.Field(typeof(Map.MapController), "_previousNode")?.GetValue(__instance) as MapNode;
                    var player = AccessTools.Field(typeof(Map.MapController), "_player")?.GetValue(__instance) as GameObject;
                    if (prevNode != null && player != null)
                        player.transform.position = prevNode.transform.position;
                }
                catch { }

                AccessTools.Method(typeof(Map.MapController), "IntroCameraPan")?.Invoke(__instance, null);
                return false;
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] IntroFade prefix check failed: {ex.Message}");
        }
        return true;
    }

    [HarmonyPatch(typeof(Map.MapController), "IntroFade")]
    [HarmonyPostfix]
    public static void MapController_IntroFade_Postfix()
    {
        if (IsHosting) MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Intro: IntroFade entered");
    }

    [HarmonyPatch(typeof(Map.MapController), "IntroCameraPan")]
    [HarmonyPostfix]
    public static void MapController_IntroCameraPan_Postfix()
    {
        if (IsHosting) MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Intro: IntroCameraPan entered");
    }

    [HarmonyPatch(typeof(Map.MapController), "PostFadeInit")]
    [HarmonyPostfix]
    public static void MapController_PostFadeInit_Postfix()
    {
        if (IsHosting) MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Intro: PostFadeInit entered");
    }

    // =========================================================================
    // BLOCK CLIENT RANDOMIZATION — prevent game from overwriting synced state
    // =========================================================================

    /// <summary>
    /// Block random map node type generation on client.
    /// MapController.Start() → rootNode.SetActiveState(NEXT) → GenerateRoomType().
    /// Without this, nodes get random types that fight with our synced types.
    /// </summary>
    [HarmonyPatch(typeof(MapNode), "GenerateRoomType")]
    [HarmonyPrefix]
    public static bool MapNode_GenerateRoomType_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    /// <summary>
    /// Skip icon generation for NONE type nodes on client.
    /// When GenerateRoomType is blocked, nodes stay NONE. GenerateIcon with NONE
    /// would crash on _icons[-1]. Let it through for valid types (our sync sets them).
    /// </summary>
    [HarmonyPatch(typeof(MapNode), "GenerateIcon")]
    [HarmonyPrefix]
    public static bool MapNode_GenerateIcon_Prefix(MapNode __instance)
    {
        if (!ShouldSuppressClientLogic) return true;
        return __instance.RoomType != RoomType.NONE;
    }

    /// <summary>
    /// Block StartShuffleAnimation on client — prevents the plunger reload animation
    /// from playing every time onDeckShuffled fires. RebuildDeckInfoDisplay handles
    /// the deck tube visuals directly without animation.
    /// </summary>
    [HarmonyPatch(typeof(DeckInfoManager), "StartShuffleAnimation")]
    [HarmonyPrefix]
    public static bool DeckInfoManager_StartShuffleAnimation_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked StartShuffleAnimation on client — host controls deck visuals");
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
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked AddInitialCoinsToBoard — host will send gold state");
        return false;
    }

    /// <summary>
    /// Capture damage text from host and dispatch to client.
    /// DamageCountDisplay.CreateText is called whenever a damage number appears.
    /// </summary>
    [HarmonyPatch(typeof(DamageCountDisplay), "CreateText")]
    [HarmonyPostfix]
    public static void DamageCountDisplay_CreateText_Postfix(string textOrLocKey, UnityEngine.Vector2 position, UnityEngine.Color color)
    {
        if (!IsHosting) return;
        if (MultiplayerPlugin.Services == null) return;
        if (!MultiplayerPlugin.Services.TryResolve<IGameEventRegistry>(out var registry)) return;

        registry.Dispatch(new Multipeglin.Events.Network.Battle.DamageTextEvent
        {
            Text = textOrLocKey,
            PosX = position.x,
            PosY = position.y,
            R = color.r,
            G = color.g,
            B = color.b,
            A = color.a,
        });
    }

    /// <summary>
    /// Block DamageCountDisplay on client — we'll render damage text from host events.
    /// </summary>
    [HarmonyPatch(typeof(DamageCountDisplay), "DisplayDamage")]
    [HarmonyPrefix]
    public static bool DamageCountDisplay_DisplayDamage_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    /// <summary>
    /// When a RegularPeg is converted to a Bomb, a NEW Bomb GameObject is created
    /// and the old peg is destroyed. Transfer the GUID from the old peg to the new
    /// Bomb so the client can still find it by GUID.
    /// We capture the old peg's GUID in a Prefix (before DestroyPeg touches state)
    /// and forcibly assign one if missing, so the Postfix can always transfer it.
    /// </summary>
    [HarmonyPatch(typeof(RegularPeg), "ConvertPegToType")]
    [HarmonyPrefix]
    public static void RegularPeg_ConvertPegToType_Prefix(RegularPeg __instance, Peg.PegType type, out string __state)
    {
        __state = null;
        if (type != Peg.PegType.BOMB) return;
        if (MultiplayerPlugin.Services == null) return;
        if (!MultiplayerPlugin.Services.TryResolve<Multipeglin.Utility.PegIdentifier>(out var pegId)) return;

        // Use GetOrAssignGuid so that even if this peg was never captured (e.g. dynamically
        // spawned by a relic/orb behaviour) we still have a stable GUID to hand to the bomb.
        __state = pegId.GetOrAssignGuid(__instance);
    }

    [HarmonyPatch(typeof(RegularPeg), "ConvertPegToType")]
    [HarmonyPostfix]
    public static void RegularPeg_ConvertPegToType_Postfix(RegularPeg __instance, Peg.PegType type, GameObject __result, string __state)
    {
        if (type != Peg.PegType.BOMB || __result == null || __result == __instance.gameObject) return;
        if (string.IsNullOrEmpty(__state)) return;
        if (MultiplayerPlugin.Services == null) return;
        if (!MultiplayerPlugin.Services.TryResolve<Multipeglin.Utility.PegIdentifier>(out var pegId)) return;

        var newBomb = __result.GetComponent<Peg>();
        if (newBomb == null)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[ClientPatch] ConvertPegToType(BOMB) returned GameObject without Peg component at " +
                $"pos=({__instance.transform.position.x:F2},{__instance.transform.position.y:F2}) oldGuid={__state}");
            return;
        }

        pegId.Register(newBomb, __state);
        MultiplayerPlugin.Logger?.LogInfo(
            $"[ClientPatch] Transferred peg GUID {__state} from RegularPeg to new Bomb at " +
            $"pos=({newBomb.transform.position.x:F2},{newBomb.transform.position.y:F2})");
    }

    /// <summary>
    /// Block client-side RNG bomb placement. The host is authoritative for which
    /// pegs become bombs. If the client runs its own ConvertPegsToBombs (via
    /// BattleController.CheckRelicsForStartingBombCount or PlayerStatusEffectController),
    /// it uses seeded RNG to pick DIFFERENT pegs than the host, producing stale
    /// bombs that never match the host's and leak into the _bombs list forever.
    /// </summary>
    [HarmonyPatch(typeof(PegManager), "ConvertPegsToBombs")]
    [HarmonyPrefix]
    public static bool PegManager_ConvertPegsToBombs_Prefix()
    {
        if (ShouldSuppressClientLogic)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PegManager.ConvertPegsToBombs — host drives bomb placement");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Block client-side delayed bomb conversion coroutine. Called via
    /// RegularPeg.StartDelayedBombConversion / LongPeg conversion paths during
    /// relic triggers. Host-driven sync reconverts via the periodic snapshot.
    /// Returns an empty IEnumerator to avoid StartCoroutine(null) crash.
    /// </summary>
    [HarmonyPatch(typeof(Peg), "WaitAndConvertToBomb")]
    [HarmonyPrefix]
    public static bool Peg_WaitAndConvertToBomb_Prefix(ref IEnumerator __result)
    {
        if (ShouldSuppressClientLogic)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked Peg.WaitAndConvertToBomb — host drives bomb placement");
            __result = EmptyEnumerator();
            return false;
        }
        return true;
    }

    private static IEnumerator EmptyEnumerator() { yield break; }

    // =========================================================================
    // LIVE PENDING DAMAGE OVERLAY — update per peg hit during coop
    // =========================================================================

    /// <summary>
    /// After each peg activation, compute the running damage total for the
    /// current player and dispatch a PendingDamagePreviewEvent so both host
    /// and client render persistent damage text above targeted enemies.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "HandlePegActivated")]
    [HarmonyPostfix]
    public static void BattleController_HandlePegActivated_Postfix(BattleController __instance)
    {
        if (!IsHosting) return;
        if (!UI.LobbyUI.GameStartReceived) return;

        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null) return;
            if (!services.TryResolve<GameState.CoopStateManager>(out var coopState)) return;
            if (coopState.TotalPlayerCount < 2) return;
            if (!services.TryResolve<IGameEventRegistry>(out var registry)) return;

            int activeSlot = coopState.ActivePlayerSlot;

            // Read BattleController's running tallies
            var pegTallyField = AccessTools.Field(typeof(BattleController), "_pegMultiplierDamageTally");
            var critField = AccessTools.Field(typeof(BattleController), "_criticalHitCount");
            var dmgMultField = AccessTools.Field(typeof(BattleController), "_damageMultiplier");
            var dmgBonusField = AccessTools.Field(typeof(BattleController), "_damageBonus");

            int pegTally = pegTallyField != null ? (int)pegTallyField.GetValue(__instance) : 0;
            int critCount = critField != null ? (int)critField.GetValue(null) : 0; // static
            float dmgMult = dmgMultField != null ? (float)dmgMultField.GetValue(__instance) : 1f;
            long dmgBonus = dmgBonusField != null ? (int)dmgBonusField.GetValue(__instance) : 0;

            // Compute running damage via AttackManager
            var amField = AccessTools.Field(typeof(BattleController), "_attackManager");
            var am = amField?.GetValue(__instance) as Battle.Attacks.AttackManager;
            if (am == null) return;

            long currentDamage = am.GetCurrentDamage(pegTally, dmgMult, dmgBonus, critCount);
            if (currentDamage <= 0 && am.isHeal) return; // heal orbs — no damage preview

            // Get target and AoE status
            bool isAoE = false;
            var attackField = AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_attack");
            var attack = attackField?.GetValue(am) as Battle.Attacks.Attack;
            if (attack is Battle.Attacks.SimpleAttack)
                isAoE = true;

            string targetGuid = null;
            var tmgr = UnityEngine.Object.FindObjectOfType<Battle.TargetingManager>();
            if (tmgr?.currentTarget != null && services.TryResolve<Utility.EnemyIdentifier>(out var eid))
                targetGuid = eid.GetGuid(tmgr.currentTarget);

            // Player name
            string playerName = $"Slot {activeSlot}";
            if (coopState.PlayerStates.TryGetValue(activeSlot, out var pState))
                playerName = pState.PlayerName ?? playerName;

            // Build event: previous players' finalized data + current player's live total
            var entries = CoopSubscriptions.GetAccumulatedDamageEntries()
                ?? new System.Collections.Generic.List<Events.Network.Coop.PendingDamagePreviewEvent.DamageEntry>();

            // Replace or add current player's live entry
            bool replaced = false;
            for (int i = 0; i < entries.Count; i++)
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
                    entry.SlotIndex, entry.PlayerName, entry.Damage,
                    entry.TargetEnemyGuid, entry.IsAoE);
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopDmgOverlay] HandlePegActivated postfix failed: {ex.Message}");
        }
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
        if (!IsHosting) return true;
        if (!UI.LobbyUI.GameStartReceived) return true;

        // Clear the pending damage overlay — the attack is now resolving
        try
        {
            UI.PendingDamageOverlay.ClearAll();
            if (MultiplayerPlugin.Services?.TryResolve<IGameEventRegistry>(out var clearReg) == true)
                clearReg.Dispatch(new Events.Network.Coop.PendingDamagePreviewEvent());
        }
        catch { }

        try
        {
            // Apply ALL players' damage directly (host included) instead of relying
            // on the ShotBehavior physics pipeline, which can fail when the attack is
            // restored from a prefab (RestoreAttackFromPrefab temp orb shots may not
            // collide with enemies reliably).
            var allShots = Events.Subscriptions.CoopSubscriptions.ConsumeNonHostShotData();
            if (allShots == null || allShots.Count == 0) return true;

            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<Utility.EnemyIdentifier>(out var enemyId) != true) return true;

            var em = UnityEngine.Object.FindObjectOfType<EnemyManager>();
            if (em == null) return true;

            foreach (var shot in allShots)
            {
                if (shot.IsHeal || shot.Damage <= 0) continue;

                // Route OnEnemyDamaged's DamageDealt tally to THIS shot's owner for the
                // duration of the damage calls below — otherwise it defaults to
                // ActivePlayerSlot (already swapped to the next round's first shooter).
                Events.Subscriptions.EnemySubscriptions.DamageAttributionSlotOverride = shot.SlotIndex;
                try
                {
                if (shot.IsAoE)
                {
                    // SimpleAttack orbs hit ALL enemies
                    for (int i = 0; i < em.Enemies.Count; i++)
                    {
                        var enemy = em.Enemies[i];
                        if (enemy == null || enemy.CurrentHealth <= 0f) continue;
                        enemy.Damage(shot.Damage, screenshake: false, 0.25f, 1f, unblockable: false,
                            Battle.Enemies.Enemy.EnemyDamageSource.AOE);
                        ApplyStatusEffectsToEnemy(enemy, shot.StatusEffectsToApply);
                    }
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[CoopAttack] {shot.PlayerName} (slot {shot.SlotIndex}): " +
                        $"AoE {shot.Damage} damage to all enemies, " +
                        $"effects={shot.StatusEffectsToApply?.Count ?? 0}");
                }
                else
                {
                    // Targeted orb — apply damage to the selected target
                    Battle.Enemies.Enemy target = null;
                    if (!string.IsNullOrEmpty(shot.TargetEnemyGuid))
                        target = enemyId.Find(shot.TargetEnemyGuid);

                    // Fallback: closest enemy
                    if (target == null || target.CurrentHealth <= 0f)
                    {
                        target = em.GetFarthestEnemyFromPlayer();
                        if (target == null) continue;
                    }

                    // Pierce: if the player's orb has ShotType.PIERCE, also damage
                    // up to N enemies behind the target (farther slot indices).
                    // Sphear is the canonical pierce orb — without this, the damage
                    // replay only hits the front enemy and pierce is invisible in coop.
                    int pierceCount = GetOrbPierceCount(shot.OrbPrefabName);
                    var pierceTargets = new System.Collections.Generic.List<Battle.Enemies.Enemy> { target };
                    if (pierceCount > 0)
                    {
                        pierceTargets.AddRange(GetEnemiesBehindTarget(em, target, pierceCount));
                    }

                    // Line-of-sight redirect: in the base game a NORMAL orb flies outward
                    // and the first enemy collider along the aim line absorbs the hit —
                    // so a front-row enemy blocks damage from reaching a back-row target.
                    // Our direct target.Damage() bypass loses that, which lets players
                    // cheese back-row enemies. Replicate ShotBehavior.RaycastShotFlight
                    // here; on any failure, fall back to the slot-based list above.
                    try
                    {
                        var raycastTargets = ResolveShotTargetsViaRaycast(
                            __instance, em, target, shot.OrbPrefabName, pierceCount);
                        if (raycastTargets != null && raycastTargets.Count > 0)
                        {
                            pierceTargets = raycastTargets;
                            target = raycastTargets[0];
                        }
                    }
                    catch (System.Exception rex)
                    {
                        MultiplayerPlugin.Logger?.LogWarning(
                            $"[CoopAttack] Raycast redirect failed for {shot.OrbPrefabName}, " +
                            $"falling back to declared target: {rex.Message}");
                    }

                    foreach (var t in pierceTargets)
                    {
                        if (t == null || t.CurrentHealth <= 0f) continue;
                        t.Damage(shot.Damage, screenshake: false, 0.25f, 1f, unblockable: false,
                            Battle.Enemies.Enemy.EnemyDamageSource.TargetedAttack);
                        ApplyStatusEffectsToEnemy(t, shot.StatusEffectsToApply);
                    }

                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[CoopAttack] {shot.PlayerName} (slot {shot.SlotIndex}): " +
                        $"{shot.Damage} damage to {target.name} (+{pierceTargets.Count - 1} pierced, " +
                        $"orb={shot.OrbPrefabName}), effects={shot.StatusEffectsToApply?.Count ?? 0}");
                }
                }
                finally
                {
                    Events.Subscriptions.EnemySubscriptions.DamageAttributionSlotOverride = -1;
                }
            }

            // Dispatch AttackStarted event to client for visual animation.
            // Use the host's (slot 0) shot data since the AttackManager_Attack_Postfix
            // won't run (we're skipping DoAttack which calls AttackManager.Attack).
            try
            {
                var hostShot = allShots.Find(s => s.SlotIndex == 0);
                if (hostShot != null && services.TryResolve<IGameEventRegistry>(out var reg))
                {
                    reg.Dispatch(new Events.Network.Battle.AttackStartedEvent
                    {
                        AnimTrigger = "attack",
                        TargetEnemyGuid = hostShot.TargetEnemyGuid,
                        NumPegsHit = hostShot.NumPegsHit,
                        IsCrit = hostShot.CriticalHitCount > 0,
                        OrbName = hostShot.OrbPrefabName,
                    });
                }
            }
            catch { }

            // Clean up the temp orb that was created for RestoreAttackFromPrefab
            // (BattleController.OnAttackStarted is invoked by StartAttacking() which
            // runs after DoAttack returns, so we don't invoke it here)
            Events.Subscriptions.CoopSubscriptions.CleanupTempOrb();

            // Zero BC tallies so any re-entry into DoAttack (e.g. the bomb-throw
            // flow that fires OnShotComplete a second time and re-runs the
            // ALL_DONE branch in CoopSubscriptions, which re-writes host's
            // tallies back to BC) cannot have the native Attack pipeline replay
            // the host's damage a second time. The first prefix call already
            // consumed _accumulatedShotData; without this zeroing, a subsequent
            // DoAttack call would find the dict empty, return true, and let
            // native Attack run on the stale tallies — double graphic + double
            // damage on host shots. Only happens intermittently because it
            // requires bomb-pegs to have been hit this shot.
            try
            {
                AccessTools.Field(typeof(BattleController), "_pegMultiplierDamageTally")?.SetValue(__instance, 0);
                AccessTools.Field(typeof(BattleController), "_numPegsHit")?.SetValue(__instance, 0);
                AccessTools.Field(typeof(BattleController), "_cactusDamageTally")?.SetValue(__instance, 0);
                AccessTools.Field(typeof(BattleController), "_criticalHitCount")?.SetValue(null, 0);
                AccessTools.Field(typeof(BattleController), "_damageMultiplier")?.SetValue(__instance, 1f);
                AccessTools.Field(typeof(BattleController), "_damageBonus")?.SetValue(__instance, 0);
            }
            catch (System.Exception tallyEx)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[CoopAttack] Failed to zero BC tallies post-consume: {tallyEx.Message}");
            }

            // Skip the original DoAttack — damage was applied directly above.
            // The caller (Update's DO_ATTACK case) still calls StartAttacking()
            // after DoAttack returns, so the state machine advances to ATTACKING.
            // EnemiesAnimating() keeps the ATTACKING state active until damage
            // animations finish.
            return false;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopAttack] Per-player damage resolution failed: {ex}");
            return true; // Fall back to original DoAttack on error
        }
    }

    /// <summary>
    /// Apply captured status effects from a non-host player's orb to a target enemy.
    /// This replicates the status effect application that the normal attack pipeline
    /// does via IAffectEnemyOnHit components and Attack.GetStatusEffects().
    /// </summary>
    private static void ApplyStatusEffectsToEnemy(
        Battle.Enemies.Enemy enemy,
        System.Collections.Generic.List<(Battle.StatusEffects.StatusEffectType Type, int Intensity)> effects)
    {
        if (effects == null || effects.Count == 0) return;
        if (enemy == null || enemy.CurrentHealth <= 0f) return;

        foreach (var (type, intensity) in effects)
        {
            if (type == Battle.StatusEffects.StatusEffectType.None) continue;
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

    private static readonly System.Collections.Generic.Dictionary<string, int> _orbPierceCache
        = new System.Collections.Generic.Dictionary<string, int>();

    /// <summary>
    /// Reads ShotBehavior._shotType / _enemiesToPierce off the orb prefab's
    /// serialized shot prefab so coop damage replay preserves pierce behavior
    /// (Sphear etc.) without running the physics pipeline. Zero = not pierce.
    /// </summary>
    private static int GetOrbPierceCount(string orbPrefabName)
    {
        if (string.IsNullOrEmpty(orbPrefabName)) return 0;
        if (_orbPierceCache.TryGetValue(orbPrefabName, out int cached)) return cached;

        int result = 0;
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
                            int enemiesToPierce = (int)(countField?.GetValue(sb) ?? 0);
                            if (shotType == Battle.Attacks.ShotBehavior.ShotType.PIERCE)
                                result = enemiesToPierce;
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
    private static System.Collections.Generic.List<Battle.Enemies.Enemy> GetEnemiesBehindTarget(
        EnemyManager em, Battle.Enemies.Enemy target, int count)
    {
        var result = new System.Collections.Generic.List<Battle.Enemies.Enemy>();
        if (em == null || target == null || count <= 0) return result;

        float targetSlot;
        try { targetSlot = em.GetSlotIndexForEnemy(target, out bool _); }
        catch { return result; }

        var candidates = new System.Collections.Generic.List<(Battle.Enemies.Enemy e, float slot)>();
        foreach (var e in em.Enemies)
        {
            if (e == null || e == target || e.CurrentHealth <= 0f) continue;
            float slot;
            try { slot = em.GetSlotIndexForEnemy(e, out bool _); } catch { continue; }
            if (slot > targetSlot)
                candidates.Add((e, slot));
        }
        candidates.Sort((a, b) => a.slot.CompareTo(b.slot));

        for (int i = 0; i < candidates.Count && result.Count < count; i++)
            result.Add(candidates[i].e);
        return result;
    }

    private struct OrbShotInfo
    {
        public Battle.Attacks.ShotBehavior.ShotType ShotType;
        public bool CanAimUp;
        public int EnemiesToPierce;
        public bool Valid;
    }

    private static readonly System.Collections.Generic.Dictionary<string, OrbShotInfo> _orbShotInfoCache
        = new System.Collections.Generic.Dictionary<string, OrbShotInfo>();

    private static OrbShotInfo GetOrbShotInfo(string orbPrefabName)
    {
        if (string.IsNullOrEmpty(orbPrefabName)) return default;
        if (_orbShotInfoCache.TryGetValue(orbPrefabName, out var cached)) return cached;

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
    private static System.Collections.Generic.List<Battle.Enemies.Enemy> ResolveShotTargetsViaRaycast(
        BattleController bc, EnemyManager em, Battle.Enemies.Enemy declaredTarget,
        string orbPrefabName, int pierceCount)
    {
        if (bc == null || em == null || declaredTarget == null) return null;

        var info = GetOrbShotInfo(orbPrefabName);
        if (info.Valid && info.ShotType == Battle.Attacks.ShotBehavior.ShotType.PINPOINT)
            return null;

        var playerField = AccessTools.Field(typeof(BattleController), "_playerTransform");
        var playerTransform = playerField?.GetValue(bc) as UnityEngine.Transform;
        if (playerTransform == null) return null;

        var offsetField = AccessTools.Field(typeof(BattleController), "_playerTransformOffset");
        var offset = (UnityEngine.Vector3)(offsetField?.GetValue(bc) ?? new UnityEngine.Vector3(1f, 0.5f, 0f));
        UnityEngine.Vector2 origin = (UnityEngine.Vector2)(playerTransform.position + offset);

        UnityEngine.Vector2 aim = ((UnityEngine.Vector2)declaredTarget.transform.position - origin).normalized;
        if (aim.sqrMagnitude < 0.0001f) return null;

        UnityEngine.Vector2 perp = UnityEngine.Vector3.Cross(aim, UnityEngine.Vector3.back).normalized;
        const float lateralOffset = 0.08f;
        var hits = new System.Collections.Generic.List<UnityEngine.RaycastHit2D>();
        hits.AddRange(UnityEngine.Physics2D.RaycastAll(origin, aim, 50f));
        hits.AddRange(UnityEngine.Physics2D.RaycastAll(origin + perp * lateralOffset, aim, 50f));
        hits.AddRange(UnityEngine.Physics2D.RaycastAll(origin - perp * lateralOffset, aim, 50f));

        bool canAimUp = !info.Valid || info.CanAimUp;
        var byEnemy = new System.Collections.Generic.Dictionary<Battle.Enemies.Enemy, float>();
        foreach (var h in hits)
        {
            if (h.collider == null) continue;
            if (!h.collider.TryGetComponent<Battle.Enemies.Enemy>(out var e)) continue;
            if (e == null || e.CurrentHealth <= 0f) continue;
            if (byEnemy.ContainsKey(e)) continue;
            // Mirror ShotBehavior filter: when canAimUp match flying==flying;
            // when !canAimUp (ground-only aim) skip flying enemies entirely.
            bool flyingOk = canAimUp
                ? declaredTarget.IsFlying == e.IsFlying
                : !e.IsFlying;
            if (!flyingOk) continue;
            byEnemy[e] = h.distance;
        }

        if (byEnemy.Count == 0) return null;

        var ordered = new System.Collections.Generic.List<Battle.Enemies.Enemy>(byEnemy.Keys);
        ordered.Sort((a, b) => byEnemy[a].CompareTo(byEnemy[b]));

        int keep = System.Math.Max(1, pierceCount + 1);
        if (ordered.Count > keep) ordered.RemoveRange(keep, ordered.Count - keep);
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

    /// <summary>Capture attack trigger, target enemy, peg count, crit, and orb name when attack starts.</summary>
    [HarmonyPatch(typeof(Battle.Attacks.AttackManager), "Attack")]
    [HarmonyPostfix]
    public static void AttackManager_Attack_Postfix(Battle.Attacks.AttackManager __instance, Battle.Enemies.Enemy target,
        int numPegsHitThisShot, int criticalHitCount)
    {
        if (!IsHosting) return;
        try
        {
            var attackField = HarmonyLib.AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_attack");
            var attack = attackField?.GetValue(__instance) as Battle.Attacks.Attack;
            LastAttackAnimTrigger = attack?.PeglinAttackAnimationTrigger ?? "attack";
            LastAttackNumPegsHit = numPegsHitThisShot;
            LastAttackIsCrit = criticalHitCount > 0;
            LastAttackOrbName = attack?.gameObject?.name?.Replace("(Clone)", "").Trim();

            if (target != null)
            {
                var enemyId = MultiplayerPlugin.Services?.TryResolve<Utility.EnemyIdentifier>(out var eid) == true ? eid : null;
                LastAttackTargetGuid = enemyId?.GetGuid(target);
            }
        }
        catch { }
    }

    // =========================================================================
    // ANIMATION SYNC — capture enemy animator changes on host
    // =========================================================================

    /// <summary>
    /// Capture Animator.SetTrigger calls on enemies and dispatch to client.
    /// This is a targeted hook — only fires when an Enemy's animator sets a trigger.
    /// </summary>
    [HarmonyPatch(typeof(UnityEngine.Animator), "SetTrigger", new[] { typeof(string) })]
    [HarmonyPostfix]
    public static void Animator_SetTrigger_Postfix(UnityEngine.Animator __instance, string name)
    {
        if (!IsHosting) return;
        if (__instance == null) return;

        // Only sync enemy animators (check if this animator belongs to an Enemy)
        var enemy = __instance.GetComponentInParent<Battle.Enemies.Enemy>();
        if (enemy == null) return;

        if (MultiplayerPlugin.Services == null) return;
        if (!MultiplayerPlugin.Services.TryResolve<IGameEventRegistry>(out var registry)) return;
        if (!MultiplayerPlugin.Services.TryResolve<Multipeglin.Utility.EnemyIdentifier>(out var enemyId)) return;

        var guid = enemyId.GetGuid(enemy);
        if (string.IsNullOrEmpty(guid)) return;

        registry.Dispatch(new Multipeglin.Events.Network.Battle.AnimationSyncEvent
        {
            EntityGuid = guid,
            ParamType = "trigger",
            ParamName = name,
            PosX = enemy.transform.position.x,
            PosY = enemy.transform.position.y,
        });
    }

    /// <summary>Capture Animator.SetBool calls on enemies.</summary>
    [HarmonyPatch(typeof(UnityEngine.Animator), "SetBool", new[] { typeof(string), typeof(bool) })]
    [HarmonyPostfix]
    public static void Animator_SetBool_Postfix(UnityEngine.Animator __instance, string name, bool value)
    {
        if (!IsHosting) return;
        if (__instance == null) return;

        var enemy = __instance.GetComponentInParent<Battle.Enemies.Enemy>();
        if (enemy == null) return;

        if (MultiplayerPlugin.Services == null) return;
        if (!MultiplayerPlugin.Services.TryResolve<IGameEventRegistry>(out var registry)) return;
        if (!MultiplayerPlugin.Services.TryResolve<Multipeglin.Utility.EnemyIdentifier>(out var enemyId)) return;

        var guid = enemyId.GetGuid(enemy);
        if (string.IsNullOrEmpty(guid)) return;

        registry.Dispatch(new Multipeglin.Events.Network.Battle.AnimationSyncEvent
        {
            EntityGuid = guid,
            ParamType = "bool",
            ParamName = name,
            Value = value ? 1 : 0,
        });
    }

    // =========================================================================
    // RNG STATE CAPTURE — host saves state before map generation
    // =========================================================================

    [HarmonyPatch(typeof(MapController), "Awake")]
    [HarmonyPrefix]
    public static void MapController_Awake_Prefix(MapController __instance)
    {
        // The game keeps exactly one MapController.instance alive via DontDestroyOnLoad.
        // Normally ProceedAfterBattle() destroys the old GO before loading the next map
        // (Act 1 ForestMap -> Act 2 CastleMap). On the client, ProceedAfterBattle never
        // runs because client battles end via the host heartbeat, not via the local win
        // flow — so the old ForestMap MC survives into CastleMap. When CastleMap's new MC
        // Awakes, the game code sees `instance != null` and self-destroys the new GO,
        // leaving the stale 37-node ForestMap _nodes as the "active" MC.
        //
        // Fix: only swap when the stale MC is from a DIFFERENT map scene (cross-act).
        // For same-scene re-entries (ForestMap -> Battle -> ForestMap) we MUST let
        // the game's default "keep old singleton, destroy new" path run — otherwise
        // every re-entry re-triggers _firstLoad == true, which re-runs PrePanWait
        // with a fresh `_playerInitialPosition` captured at the scene-default spawn
        // and produces a disoriented fast camera scroll from far away.
        if (!IsHosting && MapController.instance != null && MapController.instance != __instance)
        {
            try
            {
                var stale = MapController.instance;
                string staleScene = stale.gameObject.scene.name;
                string newScene = __instance.gameObject.scene.name;
                if (!string.Equals(staleScene, newScene, StringComparison.OrdinalIgnoreCase))
                {
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Client: destroying stale MapController from scene '{staleScene}' so new MC from '{newScene}' can take over");
                    MapController.instance = null;
                    UnityEngine.Object.Destroy(stale.gameObject);
                }
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Failed to clear stale MapController: {ex.Message}");
            }
        }

        if (IsHosting)
        {
            CapturedPreMapGenRngState = SerializeRandomState(Random.state);
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Captured pre-map-gen RNG state");
        }
        else if (ShouldSuppressClientLogic && !string.IsNullOrEmpty(PendingRngStateToRestore))
        {
            var restored = DeserializeRandomState(PendingRngStateToRestore);
            if (restored.HasValue)
            {
                Random.state = restored.Value;
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Restored host RNG state before map generation");
            }
            PendingRngStateToRestore = null;
        }
    }

    /// <summary>
    /// After MapController.Awake wires up _nodes / _player, restore the last-known map
    /// state from cache so the first rendered frame of the scene already shows the
    /// correct node icons and player position. Without this the user sees ~50ms of
    /// default-state render + a camera snap when the first heartbeat apply arrives.
    /// Runs after Awake's own assignments but before Start, so _playerInitialPosition
    /// (also captured in Awake) can be overwritten here to reflect the cached position.
    /// </summary>
    [HarmonyPatch(typeof(MapController), "Awake")]
    [HarmonyPostfix]
    public static void MapController_Awake_Postfix(MapController __instance)
    {
        if (IsHosting) return;
        if (!ShouldSuppressClientLogic) return;
        if (__instance == null) return;
        // Only the surviving instance (the new scene's MC) should apply cached state —
        // the self-destruct path in the original Awake leaves the stale GO pending
        // destruction; skip it to avoid mutating doomed nodes.
        if (MapController.instance != __instance) return;

        GameState.Appliers.MapStateApplier.ApplyCachedOnAwake(__instance, MultiplayerPlugin.Logger);
    }

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
                return true; // Let the client fire normally in PegMinigame

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
                    try { pm?.PlayerFired(); } catch { }

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
        if (!IsHosting) return;
        if (__instance == null || __instance.IsDummy) return;
        _firedBallGO = __instance.gameObject;
        _firedBallTimer = 0f;
        _firedBallLogCount = 0;
    }

    // =========================================================================
    // NODE ACTIVATION SYNC — host sends battle name when activating a node
    // =========================================================================

    [HarmonyPatch(typeof(MapNode), "ActivateNode")]
    [HarmonyPostfix]
    public static void MapNode_ActivateNode_Postfix(MapNode __instance)
    {
        if (!IsHosting) return;
        if (MultiplayerPlugin.Services == null) return;
        if (!MultiplayerPlugin.Services.TryResolve<IGameEventRegistry>(out var registry)) return;

        var pos = __instance.transform.position;
        string battleName = (__instance.MapData as MapDataBattle)?.name;
        string mapDataName = string.IsNullOrEmpty(battleName) ? __instance.MapData?.name : null;
        registry.Dispatch(new NodeActivatedEvent
        {
            PosX = pos.x,
            PosY = pos.y,
            BattleName = battleName,
            RngState = SerializeRandomState(Random.state),
            MapDataName = mapDataName,
        });
        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Host activated node at ({pos.x:F1}, {pos.y:F1}), battle={battleName}, mapData={mapDataName}");
    }

    // =========================================================================
    // CLIENT: PEG MINIGAME SPECTATING — suppress ball creation and interaction
    // =========================================================================

    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "CreateOrb")]
    [HarmonyPrefix]
    public static bool PegMinigameManager_CreateOrb_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowPegMinigameLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PegMinigameManager.CreateOrb (spectating)");
        return false;
    }

    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "PrepareNavigationOrbForFiring")]
    [HarmonyPrefix]
    public static bool PegMinigameManager_PrepareNavigationOrbForFiring_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowPegMinigameLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PegMinigameManager.PrepareNavigationOrbForFiring (spectating)");
        return false;
    }

    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "HandleRewardSlotTriggerActivated")]
    [HarmonyPrefix]
    public static bool PegMinigameManager_HandleRewardSlotTriggerActivated_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowPegMinigameLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PegMinigameManager.HandleRewardSlotTriggerActivated (spectating)");
        return false;
    }

    // Client never navigates in PegMinigame — host controls scene transitions
    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "HandleNavigationSlotTriggerActivated")]
    [HarmonyPrefix]
    public static bool PegMinigameManager_HandleNavigationSlotTriggerActivated_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    // Block FadeAndLoad: on client always (host controls navigation),
    // on host if waiting for clients to finish
    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "FadeAndLoad")]
    [HarmonyPrefix]
    public static bool PegMinigameManager_FadeAndLoad_Prefix(Peglin.PegMinigame.PegMinigameManager __instance)
    {
        // Client: always block navigation during interactive PegMinigame
        if (ShouldSuppressClientLogic)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PegMinigameManager.FadeAndLoad on client");
            return false;
        }

        // Host: gate on all clients being done
        if (IsHosting && Events.Handlers.Coop.CoopRewardState.PegMinigamePhaseActive)
        {
            if (!Events.Handlers.Coop.CoopRewardState.AllClientPegMinigameChoicesReceived)
            {
                Events.Handlers.Coop.CoopRewardState.PendingPegMinigameManager = __instance;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                MultiplayerPlugin.Logger?.LogInfo(
                    "[CoopReward] PegMinigame: host waiting for clients before navigating " +
                    $"({Events.Handlers.Coop.CoopRewardState.ClientPegMinigameChoicesReceived.Count}/{Events.Handlers.Coop.CoopRewardState.TotalPegMinigameClientsExpected})");
                return false;
            }
            MultiplayerPlugin.Logger?.LogInfo("[CoopReward] PegMinigame: all clients done, host proceeding with navigation");
        }

        return true;
    }

    // =========================================================================
    // COOP: PEG MINIGAME — independent play + wait-for-all
    // =========================================================================

    /// <summary>
    /// Prefix captures _indexSelected before HandleUpgradeOptionClicked resets it to -1.
    /// Also marks HostPegMinigameDone on the host so FadeAndLoad can gate.
    /// </summary>
    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "HandleUpgradeOptionClicked")]
    [HarmonyPrefix]
    public static void PegMinigameManager_HandleUpgradeOptionClicked_Prefix(
        PeglinUI.PostBattle.UpgradeOption.UpgradeType type,
        int ____indexSelected,
        ref int __state)
    {
        __state = ____indexSelected;

        // Mark host as done with the reward phase (before FadeAndLoad is called inside the method)
        if (____indexSelected >= 0
            && type != PeglinUI.PostBattle.UpgradeOption.UpgradeType.INSPECT_ORB_FOR_UPGRADE
            && IsHosting
            && Events.Handlers.Coop.CoopRewardState.PegMinigamePhaseActive)
        {
            Events.Handlers.Coop.CoopRewardState.HostPegMinigameDone = true;
            MultiplayerPlugin.Logger?.LogInfo("[CoopReward] PegMinigame: host finished reward selection");
        }
    }

    /// <summary>
    /// Postfix: on CLIENT, send completion event to host. On HOST, save the active player state.
    /// Each player picks their own reward independently — no sharing.
    /// </summary>
    [HarmonyPatch(typeof(Peglin.PegMinigame.PegMinigameManager), "HandleUpgradeOptionClicked")]
    [HarmonyPostfix]
    public static void PegMinigameManager_HandleUpgradeOptionClicked_Postfix(
        PeglinUI.PostBattle.UpgradeOption.UpgradeType type,
        MapDataPegMinigame ____mapData,
        int __state)
    {
        if (__state < 0) return; // no reward was selected
        if (type == PeglinUI.PostBattle.UpgradeOption.UpgradeType.INSPECT_ORB_FOR_UPGRADE) return;

        var services = MultiplayerPlugin.Services;
        if (services == null) return;

        // CLIENT: send completion event to host
        if (ShouldSuppressClientLogic && AllowPegMinigameLogic)
        {
            try
            {
                var evt = new Events.Network.Scenarios.PegMinigameCompleteEvent();

                if (type == PeglinUI.PostBattle.UpgradeOption.UpgradeType.SKIP)
                {
                    evt.Skipped = true;
                    MultiplayerPlugin.Logger?.LogInfo("[CoopReward] PegMinigame client: skipped reward");
                }
                else if (____mapData?.Rewards != null && __state < ____mapData.Rewards.Count)
                {
                    var reward = ____mapData.Rewards[__state];
                    if (reward is Peglin.PegMinigame.OrbReward orbReward && orbReward.Orb != null)
                    {
                        evt.ChosenOrbPrefabName = orbReward.Orb.name.Replace("(Clone)", "").Trim();
                        evt.OrbLevel = orbReward.Orb.GetComponent<Battle.Attacks.Attack>()?.Level ?? 0;
                        MultiplayerPlugin.Logger?.LogInfo(
                            $"[CoopReward] PegMinigame client: chose orb '{evt.ChosenOrbPrefabName}' (lvl={evt.OrbLevel})");
                    }
                    else if (reward is Peglin.PegMinigame.RelicReward relicReward && relicReward.Relic != null)
                    {
                        evt.ChosenRelicEffect = (int)relicReward.Relic.effect;
                        MultiplayerPlugin.Logger?.LogInfo(
                            $"[CoopReward] PegMinigame client: chose relic '{relicReward.Relic.locKey}'");
                    }
                }

                if (services.TryResolve<Network.IMessageSender>(out var sender))
                    sender.Send(evt);

                // Disable PegMinigame logic so subsequent CreateOrb/nav calls are blocked
                AllowPegMinigameLogic = false;
                Events.Handlers.Coop.CoopRewardState.ClientPegMinigameChoiceSent = true;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                // Flag phase so ShowWaiting() picks descriptive text; clear any stale
                // AllChoicesComplete from a prior phase that would hide the overlay.
                Events.Handlers.Coop.CoopRewardState.PegMinigamePhaseActive = true;
                Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = false;
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] PegMinigame client completion failed: {ex.Message}");
            }
            return;
        }

        // HOST: save active player state after reward granted
        if (IsHosting && Events.Handlers.Coop.CoopRewardState.PegMinigamePhaseActive)
        {
            try
            {
                if (services.TryResolve<GameState.CoopStateManager>(out var coopState))
                    coopState.SaveActivePlayerState();
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] PegMinigame host save failed: {ex.Message}");
            }
        }
    }

    // =========================================================================
    // HOST: TEXT SCENARIO REWARD SHARING — share host rewards to all coop players
    // =========================================================================

    /// <summary>
    /// When the host upgrades an orb during a TextScenario (forge, etc.),
    /// upgrade a random upgradeable orb for each non-host coop player.
    /// </summary>
    [HarmonyPatch(typeof(DeckManager), "UpgradeSpecificOrb")]
    [HarmonyPostfix]
    public static void DeckManager_UpgradeSpecificOrb_SharePostfix(GameObject toUpgrade, GameObject __result)
    {
        if (!IsHosting) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "TextScenario") return;
        if (__result == null) return;
        // When TextScenarioPhaseActive, clients handle their own dialogue — don't double-apply
        if (Events.Handlers.Coop.CoopRewardState.TextScenarioPhaseActive) return;

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) != true) return;
        if (coopState.TotalPlayerCount < 2) return;

        try
        {
            // Save host state first so the upgrade is captured
            coopState.SaveActivePlayerState();

            foreach (var kvp in coopState.PlayerStates)
            {
                if (kvp.Key == coopState.ActivePlayerSlot) continue;

                // Find upgradeable orbs in this player's deck
                var upgradeableIndices = new System.Collections.Generic.List<int>();
                for (int i = 0; i < kvp.Value.CompleteDeck.Count; i++)
                {
                    var orb = kvp.Value.CompleteDeck[i];
                    try
                    {
                        var prefab = Loading.AssetLoading.Instance?.GetOrbPrefab(orb.PrefabName);
                        if (prefab != null)
                        {
                            var attack = prefab.GetComponent<Battle.Attacks.Attack>();
                            if (attack?.NextLevelPrefab != null)
                                upgradeableIndices.Add(i);
                        }
                    }
                    catch { }
                }

                if (upgradeableIndices.Count == 0)
                {
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[CoopReward] TextScenario: slot {kvp.Key} has no upgradeable orbs");
                    continue;
                }

                int idx = upgradeableIndices[UnityEngine.Random.Range(0, upgradeableIndices.Count)];
                var orbToUpgrade = kvp.Value.CompleteDeck[idx];
                try
                {
                    var orbPrefab = Loading.AssetLoading.Instance?.GetOrbPrefab(orbToUpgrade.PrefabName);
                    var nextLevel = orbPrefab?.GetComponent<Battle.Attacks.Attack>()?.NextLevelPrefab;
                    if (nextLevel != null)
                    {
                        string newName = nextLevel.name.Replace("(Clone)", "").Trim();
                        int newLevel = nextLevel.GetComponent<Battle.Attacks.Attack>()?.Level ?? (orbToUpgrade.Level + 1);
                        kvp.Value.CompleteDeck[idx] = new GameState.SerializedOrb
                        {
                            PrefabName = newName,
                            Guid = orbToUpgrade.Guid,
                            Level = newLevel,
                        };
                        MultiplayerPlugin.Logger?.LogInfo(
                            $"[CoopReward] TextScenario: upgraded orb '{orbToUpgrade.PrefabName}' → '{newName}' for slot {kvp.Key}");
                    }
                }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogWarning(
                        $"[CoopReward] TextScenario: orb upgrade failed for slot {kvp.Key}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] TextScenario orb upgrade sharing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// When the host gains a relic during a TextScenario (forge, etc.),
    /// add the same relic to each non-host coop player's state.
    /// </summary>
    [HarmonyPatch(typeof(Relics.RelicManager), "AddRelic")]
    [HarmonyPostfix]
    public static void RelicManager_AddRelic_SharePostfix(Relics.Relic relic)
    {
        if (!IsHosting) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "TextScenario") return;
        if (relic == null) return;
        // When TextScenarioPhaseActive, clients handle their own dialogue — don't double-apply
        if (Events.Handlers.Coop.CoopRewardState.TextScenarioPhaseActive) return;

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) != true) return;
        if (coopState.TotalPlayerCount < 2) return;

        try
        {
            // Save host state first so the relic is captured
            coopState.SaveActivePlayerState();

            foreach (var kvp in coopState.PlayerStates)
            {
                if (kvp.Key == coopState.ActivePlayerSlot) continue;

                // Check if player already has this relic
                bool alreadyOwns = false;
                foreach (var r in kvp.Value.OwnedRelics)
                {
                    if (r.Effect == (int)relic.effect) { alreadyOwns = true; break; }
                }
                if (alreadyOwns) continue;

                kvp.Value.OwnedRelics.Add(new GameState.SerializedRelic
                {
                    Effect = (int)relic.effect,
                    LocKey = relic.locKey ?? "",
                    Rarity = (int)relic.globalRarity,
                });
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[CoopReward] TextScenario: added relic '{relic.locKey}' (effect={relic.effect}) to slot {kvp.Key}");
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[CoopReward] TextScenario relic sharing failed: {ex.Message}");
        }
    }

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
            int s0 = (int)t.GetField("s0", flags).GetValue(boxed);
            int s1 = (int)t.GetField("s1", flags).GetValue(boxed);
            int s2 = (int)t.GetField("s2", flags).GetValue(boxed);
            int s3 = (int)t.GetField("s3", flags).GetValue(boxed);
            return $"{s0},{s1},{s2},{s3}";
        }
        catch { return null; }
    }

    internal static Random.State? DeserializeRandomState(string s)
    {
        try
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            if (parts.Length != 4) return null;
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
        catch { return null; }
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
        if (!IsHosting) return;
        if (!UI.LobbyUI.GameStartReceived) return;

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

    // =========================================================================
    // BLOCK CLIENT STATE-ALTERING METHODS — prevent state divergence
    // =========================================================================

    /// <summary>
    /// Set to true by sync code while applying host relic state.
    /// </summary>
    internal static bool AllowRelicSync;

    [HarmonyPatch(typeof(Scenarios.ChestScenarioController), "OpenChest")]
    [HarmonyPrefix]
    public static bool ChestScenarioController_OpenChest_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowTreasureLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked ChestScenarioController.OpenChest on client");
        return false;
    }

    [HarmonyPatch(typeof(Relics.RelicManager), "AddRelic")]
    [HarmonyPrefix]
    public static bool RelicManager_AddRelic_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowRelicSync) return true;
        if (AllowNativeRewardLogic) return true;
        if (AllowShopLogic) return true;
        if (AllowTreasureLogic) return true;
        if (AllowPegMinigameLogic) return true;
        if (AllowTextScenarioLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked RelicManager.AddRelic on client");
        return false;
    }

    [HarmonyPatch(typeof(Relics.RelicManager), "RemoveRelic", new[] { typeof(Relics.Relic) })]
    [HarmonyPrefix]
    public static bool RelicManager_RemoveRelic_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowRelicSync) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked RelicManager.RemoveRelic on client");
        return false;
    }

    [HarmonyPatch(typeof(Relics.RelicManager), "RemoveRelic", new[] { typeof(Relics.RelicEffect) })]
    [HarmonyPrefix]
    public static bool RelicManager_RemoveRelicByEffect_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowRelicSync) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked RelicManager.RemoveRelic(RelicEffect) on client");
        return false;
    }

    [HarmonyPatch(typeof(Battle.Enemies.Enemy), "Damage")]
    [HarmonyPrefix]
    public static bool Enemy_Damage_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    [HarmonyPatch(typeof(Battle.Attacks.AttackManager), "Attack")]
    [HarmonyPrefix]
    public static bool AttackManager_Attack_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked AttackManager.Attack on client");
        return false;
    }

    [HarmonyPatch(typeof(DeckManager), "ShuffleCompleteDeck")]
    [HarmonyPrefix]
    public static bool DeckManager_ShuffleCompleteDeck_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (UI.LobbyUI.GameStartReceived && Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked DeckManager.ShuffleCompleteDeck on client");
        return false;
    }

    [HarmonyPatch(typeof(PlayerHealthController), "Damage")]
    [HarmonyPrefix]
    public static bool PlayerHealthController_Damage_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowNativeRewardLogic) return true;
        if (AllowShopLogic) return true;
        if (AllowTextScenarioLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PlayerHealthController.Damage on client");
        return false;
    }

    /// <summary>
    /// In coop, prevent the game from triggering game over when the active player dies
    /// unless ALL coop players are dead. Dead players just skip turns.
    /// </summary>
    [HarmonyPatch(typeof(PlayerHealthController), "CheckForDeathAndUpdateBar")]
    [HarmonyPrefix]
    public static bool PlayerHealthController_CheckForDeathAndUpdateBar_Prefix(PlayerHealthController __instance)
    {
        if (!IsHosting || !UI.LobbyUI.GameStartReceived) return true;

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) != true) return true;
        if (coopState.TotalPlayerCount < 2) return true;

        // If health is still above 0, let the normal update run (just updates the bar)
        var healthField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_playerHealth");
        var healthVar = healthField?.GetValue(__instance);
        if (healthVar == null) return true;

        var valueProp = healthVar.GetType().GetProperty("Value");
        if (valueProp == null) return true;
        float hp = (float)valueProp.GetValue(healthVar);

        if (hp > 0f) return true; // not dead, let normal flow update the bar

        // Active player is dead. Only allow game over if ALL players are dead.
        if (!coopState.AllPlayersDead)
        {
            // Clamp the FloatVariable to 0 so downstream reads (CoopState save,
            // heartbeat health sync, UI bar) don't see negative HP. Without this,
            // the dead player displays as -5/100 and TurnManager may race with
            // native flow that reads the negative value mid-cleanup.
            try
            {
                var setMethod = healthVar.GetType().GetMethod("Set", new[] { typeof(float) });
                if (setMethod != null && hp < 0f)
                    setMethod.Invoke(healthVar, new object[] { 0f });
            }
            catch { }

            // Also clamp the active player's stored CoopPlayerState so TurnManager
            // reads exactly 0 (not a negative) when deciding to skip this slot.
            var activeState = coopState.GetPlayerState(coopState.ActivePlayerSlot);
            if (activeState != null && activeState.CurrentHealth < 0f)
                activeState.CurrentHealth = 0f;

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatches] Active player (slot {coopState.ActivePlayerSlot}) died but other players alive — suppressing game over, clamped hp to 0");
            return false; // block CheckForDeathAndUpdateBar entirely
        }

        // All players dead — allow normal game over
        return true;
    }

    /// <summary>
    /// In coop, <c>PlayerHealthController</c> is a single singleton that the host
    /// hot-swaps between slots as turns change. When a non-host player (e.g. client
    /// slot 1) fires Restorb, the host's native <c>Heal()</c> spawns a floating
    /// "+N" popup and heal particle effect at the PHC's own transform — which is
    /// the host's (slot 0) visual position. Visually the heal looks like it hit
    /// the wrong player.
    ///
    /// Suppress the native VFX fields when the active slot isn't the local host
    /// slot (0). The <c>CoopPlayerVisuals</c> HP text still updates via heartbeat
    /// so the actual healed slot shows the new HP value. We restore the fields
    /// in the postfix so normal single-player / host-turn heals still render.
    /// </summary>
    [HarmonyPatch(typeof(PlayerHealthController), "Heal")]
    [HarmonyPrefix]
    public static void PlayerHealthController_Heal_Prefix(PlayerHealthController __instance, ref object[] __state)
    {
        __state = null;
        if (!IsHosting) return;

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) != true) return;
        if (coopState.TotalPlayerCount < 2) return;
        if (coopState.ActivePlayerSlot == 0) return; // heal is for the host itself — let VFX play

        try
        {
            var floatingTextField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_damageFloatingTextPrefab");
            var healSfxField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_healSFX");
            var particleField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "HealParticleAnim");

            __state = new object[]
            {
                floatingTextField, floatingTextField?.GetValue(__instance),
                healSfxField, healSfxField?.GetValue(__instance),
                particleField, particleField?.GetValue(__instance),
            };

            floatingTextField?.SetValue(__instance, null);
            healSfxField?.SetValue(__instance, null);
            particleField?.SetValue(__instance, null);
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Heal VFX suppression failed: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(PlayerHealthController), "Heal")]
    [HarmonyPostfix]
    public static void PlayerHealthController_Heal_Postfix(PlayerHealthController __instance, object[] __state)
    {
        if (__state == null) return;
        try
        {
            var floatingTextField = __state[0] as System.Reflection.FieldInfo;
            var floatingTextVal = __state[1];
            var healSfxField = __state[2] as System.Reflection.FieldInfo;
            var healSfxVal = __state[3];
            var particleField = __state[4] as System.Reflection.FieldInfo;
            var particleVal = __state[5];

            floatingTextField?.SetValue(__instance, floatingTextVal);
            healSfxField?.SetValue(__instance, healSfxVal);
            particleField?.SetValue(__instance, particleVal);
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Heal VFX restore failed: {ex.Message}");
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
        if (!IsHosting) return true;

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) != true) return true;
        if (coopState.TotalPlayerCount < 2) return true;

        var phc = UnityEngine.Object.FindObjectOfType<PlayerHealthController>();
        if (phc == null) return true;

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
                if (hostState != null) hostState.CurrentHealth = 1;

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

    /// <summary>
    /// Hide the native HP bar + health text when in a multiplayer session (host or
    /// client). The per-player HP bars rendered under each sprite replace it, and
    /// the freed canvas slot is used for the Skip Turn button.
    /// </summary>
    [HarmonyPatch(typeof(PlayerHealthController), "OnEnable")]
    [HarmonyPostfix]
    public static void PlayerHealthController_OnEnable_Postfix(PlayerHealthController __instance)
    {
        try
        {
            if (MultiplayerPlugin.Services == null) return;
            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)) return;
            if (!mode.IsHosting && !mode.IsSpectating) return;

            var healthTextField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_healthText");
            if (healthTextField?.GetValue(__instance) is UnityEngine.Component healthText && healthText != null)
                healthText.gameObject.SetActive(false);

            var barField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_barScript");
            if (barField?.GetValue(__instance) is UnityEngine.Component barScript && barScript != null)
                barScript.gameObject.SetActive(false);
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] Hide HP bar failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Set to true by sync code while applying host gold state.
    /// </summary>
    internal static bool AllowCurrencySync;

    [HarmonyPatch(typeof(Currency.CurrencyManager), "AddGold")]
    [HarmonyPrefix]
    public static bool CurrencyManager_AddGold_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowCurrencySync) return true;
        if (AllowNativeRewardLogic) return true;
        if (AllowShopLogic) return true;
        if (AllowTreasureLogic) return true;
        if (AllowTextScenarioLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked CurrencyManager.AddGold on client");
        return false;
    }

    [HarmonyPatch(typeof(Currency.CurrencyManager), "RemoveGold")]
    [HarmonyPrefix]
    public static bool CurrencyManager_RemoveGold_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowCurrencySync) return true;
        if (AllowNativeRewardLogic) return true;
        if (AllowShopLogic) return true;
        if (AllowTextScenarioLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked CurrencyManager.RemoveGold on client");
        return false;
    }

    /// <summary>
    /// Post-battle gold deduction sync: when the client spends gold on the native
    /// BattleUpgradeCanvas (heal, max HP, orb upgrade, orb add), notify the host
    /// immediately so CoopPlayerState.Gold is updated before the next heartbeat
    /// (which would otherwise reset the client's local gold to the stale value).
    /// </summary>
    [HarmonyPatch(typeof(Currency.CurrencyManager), "RemoveGold")]
    [HarmonyPostfix]
    public static void CurrencyManager_RemoveGold_Postfix(int amount)
    {
        if (!ShouldSuppressClientLogic) return;
        if (!AllowNativeRewardLogic) return;
        if (AllowCurrencySync) return;
        if (amount <= 0) return;
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
            {
                // Defer send to the next Update tick so the post-purchase HP change
                // is observable: native reward flows (Heal / AdjustMaxHealth) run on
                // the same frame as RemoveGold, so reading HP here returns the stale
                // pre-heal value. Enqueue on MainThreadDispatcher runs next frame.
                var evt = new Events.Network.Coop.PostBattleGoldSpentEvent { Amount = amount };
                var dispatcher = Multipeglin.Utility.MainThreadDispatcher.Instance;
                if (dispatcher != null)
                {
                    dispatcher.Enqueue(() =>
                    {
                        try
                        {
                            var hc = UnityEngine.Object.FindObjectOfType<Battle.PlayerHealthController>();
                            evt.CurrentHealth = hc != null ? hc.CurrentHealth : -1f;
                            evt.MaxHealth = hc != null ? hc.MaxHealth : 0f;
                            sender.Send(evt);
                            MultiplayerPlugin.Logger?.LogInfo(
                                $"[ClientPatch] Sent PostBattleGoldSpentEvent amount={evt.Amount} hp={evt.CurrentHealth}/{evt.MaxHealth}");
                        }
                        catch (System.Exception ex2)
                        {
                            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] Deferred send failed: {ex2.Message}");
                        }
                    });
                }
                else
                {
                    // Fallback: send immediately, HP fields unset
                    evt.CurrentHealth = -1f;
                    sender.Send(evt);
                }
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] Failed to send PostBattleGoldSpentEvent: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(DeckManager), "AddOrbToDeck")]
    [HarmonyPrefix]
    public static bool DeckManager_AddOrbToDeck_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowNativeRewardLogic) return true;
        if (AllowShopLogic) return true;
        if (AllowPegMinigameLogic) return true;
        if (AllowTextScenarioLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked DeckManager.AddOrbToDeck on client");
        return false;
    }

    [HarmonyPatch(typeof(DeckManager), "RemoveOrbFromBattleDeck")]
    [HarmonyPrefix]
    public static bool DeckManager_RemoveOrbFromBattleDeck_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked DeckManager.RemoveOrbFromBattleDeck on client");
        return false;
    }

    // =========================================================================
    // POST-BATTLE REWARD PHASE — intercept navigation for coop sync
    // =========================================================================

    /// <summary>
    /// Intercept PostBattleController.StartNavigation to synchronize the coop
    /// post-battle reward phase. On host: delay navigation until all clients
    /// finish. On client: block navigation entirely and send results to host.
    /// </summary>
    [HarmonyPatch(typeof(Battle.PostBattleController), "StartNavigation")]
    [HarmonyPrefix]
    public static bool PostBattleController_StartNavigation_Prefix(Battle.PostBattleController __instance)
    {
        if (!UI.LobbyUI.GameStartReceived) return true; // not coop

        var services = MultiplayerPlugin.Services;
        if (services == null) return true;

        if (IsHosting)
        {
            if (!Events.Handlers.Coop.CoopRewardState.HostRewardPhaseActive)
                return true; // not in coop reward phase — normal flow

            // Save host's updated state after rewards
            if (services.TryResolve<GameState.CoopStateManager>(out var coopState))
                coopState.SaveActivePlayerState();

            Events.Handlers.Coop.CoopRewardState.HostRewardsDone = true;
            Events.Handlers.Coop.CoopRewardState.PendingPostBattleController = __instance;

            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Host finished post-battle rewards, checking if all clients done...");

            if (Events.Handlers.Coop.CoopRewardState.AllClientRewardChoicesReceived)
            {
                // All done — proceed with navigation
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] All clients done — proceeding with navigation");
                Events.Handlers.Coop.CoopRewardState.HostRewardPhaseActive = false;
                AllowNativeRewardLogic = false;

                if (services.TryResolve<IGameEventRegistry>(out var reg))
                    reg.Dispatch(new Events.Network.Coop.AllChoicesCompleteEvent { Phase = "post_battle" });

                return true; // let StartNavigation run
            }
            else
            {
                // Still waiting for clients
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[ClientPatches] Waiting for clients: {Events.Handlers.Coop.CoopRewardState.ClientRewardChoicesReceived.Count}/{Events.Handlers.Coop.CoopRewardState.TotalRewardClientsExpected}");
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                return false; // block navigation until all clients done
            }
        }
        else if (ShouldSuppressClientLogic)
        {
            // Client: never navigate — send results to host and wait
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Client finished post-battle rewards — sending results to host");

            try
            {
                var completeEvent = CaptureClientPostBattleState();
                if (services.TryResolve<Network.IMessageSender>(out var sender))
                    sender.Send(completeEvent);
            }
            catch (System.Exception ex)
            {
                MultiplayerPlugin.Logger?.LogError($"[ClientPatches] Failed to send PostBattleCompleteEvent: {ex.Message}");
            }

            // Disable reward logic bypass now that the screen is done
            AllowNativeRewardLogic = false;

            // Clear relic choice tracking
            ClientChosenPostBattleRelicEffect = -1;
            ClientChosenPostBattleRelicName = null;
            Events.Handlers.Coop.CoopRewardState.PendingPostBattleRelicChoices = null;

            // Show waiting overlay
            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
            return false; // never navigate on client
        }

        return true;
    }

    /// <summary>
    /// Capture the client's current state after the reward screen for sending to host.
    /// </summary>
    private static Events.Network.Coop.PostBattleCompleteEvent CaptureClientPostBattleState()
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
            evt.Gold = currency.GoldAmount;

        // Deck
        var completeDeck = DeckManager.completeDeck;
        if (completeDeck != null)
        {
            foreach (var orbGO in completeDeck)
            {
                if (orbGO == null) continue;
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
    // HOST: WEIGHTED CHIP (SLOT_MULTIPLIERS) — SKIP DURING CLIENT TURNS
    // =========================================================================

    /// <summary>
    /// Weighted Chip adds multiplier zones (0.5x, 1x, 2x) and fire pits at the
    /// bottom of the pegboard. These belong to the host's relic set — they should
    /// only affect the host's shots, not the client's.
    /// During client turns: skip both the damage multiplier and fire pit damage.
    /// Still allow inhale zone activation (separate relic mechanic).
    /// </summary>
    [HarmonyPatch(typeof(SpecialSlotController), "SlotActivated")]
    [HarmonyPrefix]
    public static bool SpecialSlotController_SlotActivated_Prefix(
        int index,
        BattleController ____battleController,
        int ____inhaleSlot)
    {
        if (!IsHosting) return true;
        if (!UI.LobbyUI.GameStartReceived) return true;
        if (Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn) return true;

        // Client's turn — skip multiplier + fire pit damage from host's Weighted Chip.
        // Still handle inhale zone (separate relic) if applicable.
        if (____battleController != null && !____battleController.IsNavigating()
            && index == ____inhaleSlot)
        {
            SpecialSlotController.OrbInhaled?.Invoke();
        }

        return false;
    }

    // =========================================================================
    // HOST: SUPPRESS PREDICTION TRAJECTORY DURING CLIENT TURNS
    // =========================================================================

    /// <summary>
    /// When it's a client's turn in coop, prevent PredictionManager from rendering
    /// the host's dotted trajectory. PachinkoBall.Arm() re-enables the line renderer
    /// when the next ball enters AIMING state, and Update() keeps calling Predict().
    /// Block both to keep the host screen clean during client turns.
    /// </summary>
    [HarmonyPatch(typeof(PredictionManager), nameof(PredictionManager.Predict))]
    [HarmonyPrefix]
    public static bool PredictionManager_Predict_Prefix()
    {
        if (!IsHosting) return true;
        if (!UI.LobbyUI.GameStartReceived) return true;
        if (Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn) return true;
        // Allow prediction during non-battle phases (navigation, post-battle, map).
        // The turn system only tracks battle turns — outside active combat the host
        // should always see the aimer (e.g. navigation orb after victory).
        var state = BattleController.CurrentBattleState;
        if (state == BattleController.BattleState.NAVIGATION
            || state == BattleController.BattleState.AWAITING_POST_BATTLE_CONTROLLER
            || state == BattleController.BattleState.NAVIGATION_COMPLETE)
            return true;
        return false; // Not host's turn — suppress prediction
    }

    [HarmonyPatch(typeof(PredictionManager), nameof(PredictionManager.SetLineRendererStatus))]
    [HarmonyPrefix]
    public static bool PredictionManager_SetLineRendererStatus_Prefix(bool status)
    {
        if (!status) return true; // Always allow disabling
        if (!IsHosting) return true;
        if (!UI.LobbyUI.GameStartReceived) return true;
        if (Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn) return true;
        var state = BattleController.CurrentBattleState;
        if (state == BattleController.BattleState.NAVIGATION
            || state == BattleController.BattleState.AWAITING_POST_BATTLE_CONTROLLER
            || state == BattleController.BattleState.NAVIGATION_COMPLETE)
            return true;
        return false; // Not host's turn — don't re-enable prediction line
    }

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
        if (!ShouldSuppressClientLogic) return;
        if (!UI.LobbyUI.GameStartReceived) return;
        if (!Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn) return;

        try
        {
            string guid = null;
            if (enemy != null)
            {
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Utility.EnemyIdentifier>(out var eid) == true)
                    guid = eid.GetGuid(enemy);
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

    // =========================================================================
    // CLIENT: TEXT SCENARIO SPECTATING — block dialogue + navigation on client
    // =========================================================================

    /// <summary>
    /// Flag to allow mirror event logic on the client (deck modification).
    /// </summary>
    public static bool AllowMirrorEventLogic;

    /// <summary>
    /// Allow DisableFadeCurtain on client so the black curtain fades out and
    /// the TextScenario scene (background, doodads) is visible. The native
    /// Dialogue System UI will render on the client with correct fonts/layout.
    /// </summary>
    [HarmonyPatch(typeof(DialogueSystemScenario), "DisableFadeCurtain")]
    [HarmonyPrefix]
    public static bool DialogueSystemScenario_DisableFadeCurtain_Prefix()
    {
        // Always allow — the curtain must fade out on the client too.
        // StartConversation (called inside DisableFadeCurtain) is also allowed
        // so the native dialogue UI renders. Response clicks are blocked separately.
        if (ShouldSuppressClientLogic)
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] ALLOWING DisableFadeCurtain on client (native dialogue UI)");
        return true;
    }

    /// <summary>
    /// Allow StartConversation on client so the native Dialogue System UI renders
    /// with proper fonts, sizing, and layout. The client sees the same dialogue as
    /// the host. Response button clicks are blocked separately.
    /// </summary>
    [HarmonyPatch(typeof(DialogueManager), "StartConversation", typeof(string), typeof(UnityEngine.Transform), typeof(UnityEngine.Transform), typeof(int))]
    [HarmonyPrefix]
    public static bool DialogueManager_StartConversation_Prefix()
    {
        if (ShouldSuppressClientLogic)
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] ALLOWING StartConversation on client (native dialogue UI)");
        return true;
    }

    /// <summary>
    /// DialogueSystemScenario.ConversationEnded —
    /// Client: allow when AllowTextScenarioLogic (let native dialogue flow complete, then capture state).
    /// Host: in coop, do wait-for-all before allowing StartNavigation.
    /// </summary>
    [HarmonyPatch(typeof(DialogueSystemScenario), "ConversationEnded")]
    [HarmonyPrefix]
    public static bool DialogueSystemScenario_ConversationEnded_Prefix(DialogueSystemScenario __instance)
    {
        if (!UI.LobbyUI.GameStartReceived)
            return true; // Not in coop

        if (ShouldSuppressClientLogic)
        {
            if (AllowTextScenarioLogic)
            {
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] ALLOWING DialogueSystemScenario.ConversationEnded (AllowTextScenarioLogic)");
                // Don't call StartNavigation on client — just capture state and send to host.
                // We return false and handle the post-dialogue logic ourselves.
                CaptureAndSendTextScenarioState();
                return false;
            }
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked DialogueSystemScenario.ConversationEnded (spectating)");
            return false;
        }

        if (IsHosting && Events.Handlers.Coop.CoopRewardState.TextScenarioPhaseActive)
        {
            // HOST: save own state first
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) == true)
                coopState.SaveActivePlayerState();

            Events.Handlers.Coop.CoopRewardState.HostTextScenarioDone = true;

            if (Events.Handlers.Coop.CoopRewardState.AllClientTextScenarioChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host ConversationEnded — all clients done, proceeding");
                Events.Handlers.Coop.CoopRewardState.TextScenarioPhaseActive = false;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = false;
                if (services?.TryResolve<Events.IGameEventRegistry>(out var reg) == true)
                    reg.Dispatch(new Events.Network.Coop.AllChoicesCompleteEvent { Phase = "text_scenario" });
                return true; // Let ConversationEnded run normally (calls StartNavigation)
            }
            else
            {
                // Not all clients done — store reference and block
                Events.Handlers.Coop.CoopRewardState.PendingDialogueSystemScenario = __instance;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host ConversationEnded — waiting for clients to finish TextScenario");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Captures the client's current deck/health/gold/relics after a TextScenario
    /// dialogue completes and sends a TextScenarioCompleteEvent to the host.
    /// </summary>
    private static void CaptureAndSendTextScenarioState()
    {
        if (Events.Handlers.Coop.CoopRewardState.ClientTextScenarioChoiceSent) return;

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
                    if (orbGo == null) continue;
                    var attack = orbGo.GetComponent<Battle.Attacks.Attack>();
                    var prefabName = orbGo.name.Replace("(Clone)", "").Trim();
                    string guid = orbId?.GetGuid(orbGo);
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
                if (rm == null) continue;
                var ownedDict = ownedRelicsField?.GetValue(rm)
                    as System.Collections.Generic.Dictionary<Relics.RelicEffect, Relics.Relic>;
                if (ownedDict == null) continue;
                foreach (var kvp in ownedDict)
                {
                    var relic = kvp.Value;
                    if (relic == null) continue;
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
    /// Block response button clicks on client during TextScenario unless
    /// AllowTextScenarioLogic is set (client making its own dialogue choices).
    /// </summary>
    [HarmonyPatch(typeof(StandardUIResponseButton), "OnClick")]
    [HarmonyPrefix]
    public static bool StandardUIResponseButton_OnClick_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        if (AllowTextScenarioLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked StandardUIResponseButton.OnClick (spectating)");
        return false;
    }

    /// <summary>
    /// Allow OnContinue on client — advancing through text pages is harmless
    /// (doesn't affect game state). Only response clicks are blocked.
    /// The client can read through dialogue at their own pace.
    /// </summary>

    /// <summary>
    /// Track navigation state on host; block navigation on client unless we
    /// explicitly enable it for spectating the navigation shot.
    /// </summary>
    [HarmonyPatch(typeof(DialogueSystemScenario), "StartNavigation")]
    [HarmonyPrefix]
    public static bool DialogueSystemScenario_StartNavigation_Prefix()
    {
        // On host: track that navigation has started
        if (IsHosting)
        {
            TextScenarioHoverTracker.IsNavigating = true;
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Host TextScenario navigation started");
            return true;
        }

        // On client: block unless navigation is already enabled by heartbeat
        if (ShouldSuppressClientLogic && !AllowTextScenarioNavigation)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked DialogueSystemScenario.StartNavigation (spectating)");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Flag set by the state applier when the heartbeat reports IsNavigating=true.
    /// Allows the client to activate its navigation controller.
    /// </summary>
    public static bool AllowTextScenarioNavigation;

    // =========================================================================
    // HOST: TRACK DIALOGUE HOVER (which response button host is highlighting)
    // =========================================================================

    [HarmonyPatch(typeof(StandardUIResponseButton), "OnSelect")]
    [HarmonyPostfix]
    public static void StandardUIResponseButton_OnSelect_Postfix(StandardUIResponseButton __instance)
    {
        if (!IsHosting) return;

        try
        {
            // Find this button's index in its parent menu panel
            var dialogueUI = UnityEngine.Object.FindObjectOfType<StandardDialogueUI>();
            if (dialogueUI == null) return;

            var menuPanel = dialogueUI.conversationUIElements?.defaultMenuPanel;
            if (menuPanel?.buttons == null) return;

            for (int i = 0; i < menuPanel.buttons.Length; i++)
            {
                if (menuPanel.buttons[i] == __instance)
                {
                    TextScenarioHoverTracker.CurrentHoveredIndex = i;
                    return;
                }
            }
        }
        catch { }
    }

    // =========================================================================
    // SHOP + TREASURE: Wait-for-all synchronization
    // =========================================================================

    /// <summary>
    /// Replace the client's SetUpRelicOffer with a version that uses the host's
    /// chosen relic effects (synced via MapStateSnapshot.SeededShopRelicEffects).
    /// The original path dequeues from AllCommonRelicsRandomQueue, which is empty
    /// on the client because the shuffle that populates it uses RNG (suppressed).
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.Shop.ShopManager), "SetUpRelicOffer")]
    [HarmonyPrefix]
    public static bool ShopManager_SetUpRelicOffer_Prefix(Scenarios.Shop.ShopManager __instance)
    {
        if (!ShouldSuppressClientLogic) return true;
        try
        {
            ShopRelicSyncState.CurrentShopManager = __instance;
            ShopRelicSyncState.PopulateShopRelics(__instance, MultiplayerPlugin.Logger);
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ShopRelicSync] Prefix failed: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Clear shop relic sync state when the shop closes so stale references from
    /// a prior visit don't get reused on the next shop.
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.Shop.ShopManager), "CloseStore")]
    [HarmonyPostfix]
    public static void ShopManager_CloseStore_Postfix()
    {
        ShopRelicSyncState.CurrentShopManager = null;
        ShopRelicSyncState.LatestRelicEffects = null;
    }

    /// <summary>
    /// Client-side: tracks purchases made during the current shop visit.
    /// Populated by PurchaseItem postfix, sent to host on CloseStore.
    /// </summary>
    internal static readonly System.Collections.Generic.List<Events.Network.Scenarios.ShopPurchase> ClientShopPurchases
        = new System.Collections.Generic.List<Events.Network.Scenarios.ShopPurchase>();

    /// <summary>Client-side: gold at the time the shop was entered.</summary>
    internal static int ClientShopStartGold;

    /// <summary>
    /// Track purchases on the client so we can send them to the host on shop exit.
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.Shop.ShopManager), "PurchaseItem")]
    [HarmonyPostfix]
    public static void ShopManager_PurchaseItem_Postfix(Scenarios.Shop.IPurchasableItem item)
    {
        if (!ShouldSuppressClientLogic) return;
        if (!AllowShopLogic) return;

        try
        {
            var purchase = new Events.Network.Scenarios.ShopPurchase();
            if (item is Scenarios.Shop.PurchasableOrb orbItem)
            {
                purchase.Type = "orb";
                // Get the orb prefab name from the PurchasableOrb via reflection
                var prefabField = HarmonyLib.AccessTools.Field(typeof(Scenarios.Shop.PurchasableOrb), "_orbPrefab");
                var prefab = prefabField?.GetValue(orbItem) as UnityEngine.GameObject;
                purchase.Name = prefab?.name?.Replace("(Clone)", "").Trim() ?? "unknown";
                purchase.Cost = item.GetCost();
            }
            else if (item is Scenarios.Shop.PurchasableRelic relicItem)
            {
                purchase.Type = "relic";
                var relicField = HarmonyLib.AccessTools.Field(typeof(Scenarios.Shop.PurchasableRelic), "_relic");
                var relic = relicField?.GetValue(relicItem) as Relics.Relic;
                purchase.Name = relic?.locKey ?? "unknown";
                purchase.RelicEffect = relic != null ? (int)relic.effect : -1;
                purchase.Cost = item.GetCost();
            }

            ClientShopPurchases.Add(purchase);
            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatch] Tracked shop purchase: {purchase.Type} '{purchase.Name}' cost={purchase.Cost}");

            // Send immediately so host deducts gold + applies orb/relic BEFORE the
            // next heartbeat. Without this, the heartbeat syncs the old (stale) gold
            // value back to the client between purchases, masking the deduction.
            try
            {
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
                {
                    sender.Send(new Events.Network.Scenarios.ShopPurchaseEvent
                    {
                        Type = purchase.Type,
                        Name = purchase.Name,
                        Cost = purchase.Cost,
                        RelicEffect = purchase.RelicEffect,
                    });
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[ClientPatch] Sent ShopPurchaseEvent: {purchase.Type} '{purchase.Name}' cost={purchase.Cost}");
                }
            }
            catch (System.Exception sendEx)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] Failed to send ShopPurchaseEvent: {sendEx.Message}");
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] Failed to track shop purchase: {ex.Message}");
        }
    }

    /// <summary>
    /// ShopManager.CloseStore — on client: send ShopCompleteEvent, block navigation.
    /// On host: check wait-for-all before allowing navigation to proceed.
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.Shop.ShopManager), "CloseStore")]
    [HarmonyPrefix]
    public static bool ShopManager_CloseStore_Prefix(Scenarios.Shop.ShopManager __instance)
    {
        if (!UI.LobbyUI.GameStartReceived) return true; // Not in coop

        if (ShouldSuppressClientLogic)
        {
            // CLIENT: first click sends ShopCompleteEvent and shows waiting overlay.
            // Subsequent clicks are silently ignored — the overlay already covers
            // the screen, but the EventSystem can still route clicks to the
            // button if the overlay canvas somehow doesn't block them.
            if (Events.Handlers.Coop.CoopRewardState.ClientShopChoiceSent)
                return false;

            try
            {
                AllowShopLogic = false;

                int remainingGold = Currency.CurrencyManager.Instance?.GoldAmount ?? 0;
                int goldSpent = ClientShopStartGold - remainingGold;

                var evt = new Events.Network.Scenarios.ShopCompleteEvent
                {
                    Purchases = new System.Collections.Generic.List<Events.Network.Scenarios.ShopPurchase>(ClientShopPurchases),
                    GoldSpent = goldSpent > 0 ? goldSpent : 0,
                    RemainingGold = remainingGold,
                };

                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
                {
                    sender.Send(evt);
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatch] Sent ShopCompleteEvent: {evt.Purchases.Count} purchases, gold={evt.RemainingGold}");
                }

                ClientShopPurchases.Clear();
                Events.Handlers.Coop.CoopRewardState.ClientShopChoiceSent = true;
            }
            catch (System.Exception ex)
            {
                MultiplayerPlugin.Logger?.LogError($"[ClientPatch] Failed to send ShopCompleteEvent: {ex.Message}");
            }

            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
            Events.Handlers.Coop.CoopRewardState.ShopPhaseActive = true;
            // Reset stale AllChoicesComplete from prior phases — otherwise
            // CoopRewardUI.Update will early-return and never show the overlay.
            Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = false;
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Client finished shopping — waiting for other players");
            return false; // Block CloseStore navigation on client
        }

        if (IsHosting && Events.Handlers.Coop.CoopRewardState.ShopPhaseActive)
        {
            // HOST: idempotent — if already done and waiting, just silently block.
            if (Events.Handlers.Coop.CoopRewardState.HostShopDone
                && !Events.Handlers.Coop.CoopRewardState.AllClientShopChoicesReceived)
            {
                return false; // Still waiting — no log spam, overlay already visible.
            }

            // Mark self as done, check if all clients finished
            Events.Handlers.Coop.CoopRewardState.HostShopDone = true;
            // Reset stale AllChoicesComplete from prior phases so the overlay appears.
            Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = false;

            if (Events.Handlers.Coop.CoopRewardState.AllClientShopChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host CloseStore — all clients done, proceeding");
                Events.Handlers.Coop.CoopRewardState.ShopPhaseActive = false;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = false;
                Events.Handlers.Coop.CoopRewardState.ShopCompletionProceeded = true;
                // Dispatch AllChoicesComplete so clients transition to the
                // "Waiting for host to pick next stage..." overlay.
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Events.IGameEventRegistry>(out var reg) == true)
                    reg.Dispatch(new Events.Network.Coop.AllChoicesCompleteEvent { Phase = "shop" });
                return true; // Let CloseStore run normally
            }
            else
            {
                // Not all clients done — store reference, flag waiting, block.
                Events.Handlers.Coop.CoopRewardState.PendingShopManager = __instance;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host CloseStore — waiting for clients to finish shopping");
                return false; // Block until all clients done
            }
        }

        return true;
    }

    /// <summary>
    /// ChestScenarioController.Skip — controls navigation after treasure relic selection.
    /// On client: send TreasureCompleteEvent, block navigation.
    /// On host: check wait-for-all before allowing navigation.
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.ChestScenarioController), "Skip")]
    [HarmonyPrefix]
    public static bool ChestScenarioController_Skip_Prefix(Scenarios.ChestScenarioController __instance)
    {
        if (!UI.LobbyUI.GameStartReceived) return true; // Not in coop

        if (ShouldSuppressClientLogic)
        {
            // CLIENT: send treasure complete if not already sent
            if (!Events.Handlers.Coop.CoopRewardState.ClientTreasureChoiceSent)
            {
                try
                {
                    // If we get here without AcceptRelic having fired, the player skipped
                    var services = MultiplayerPlugin.Services;
                    if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
                    {
                        sender.Send(new Events.Network.Scenarios.TreasureCompleteEvent
                        {
                            ChosenRelicEffect = -1,
                            ChosenRelicName = null,
                        });
                        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Client skipped treasure relic — sent TreasureCompleteEvent");
                    }
                    Events.Handlers.Coop.CoopRewardState.ClientTreasureChoiceSent = true;
                }
                catch (System.Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogError($"[ClientPatch] Failed to send TreasureCompleteEvent: {ex.Message}");
                }
            }

            AllowTreasureLogic = false;
            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
            Events.Handlers.Coop.CoopRewardState.TreasurePhaseActive = true;
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Client finished treasure — waiting for other players");
            return false; // Block navigation on client
        }

        if (IsHosting && Events.Handlers.Coop.CoopRewardState.TreasurePhaseActive)
        {
            // HOST: mark self as done, check if all clients finished
            Events.Handlers.Coop.CoopRewardState.HostTreasureDone = true;

            if (Events.Handlers.Coop.CoopRewardState.AllClientTreasureChoicesReceived)
            {
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host Skip — all clients done, proceeding");
                Events.Handlers.Coop.CoopRewardState.TreasurePhaseActive = false;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = false;
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Events.IGameEventRegistry>(out var reg) == true)
                    reg.Dispatch(new Events.Network.Coop.AllChoicesCompleteEvent { Phase = "treasure" });
                return true; // Let Skip run normally
            }
            else
            {
                Events.Handlers.Coop.CoopRewardState.PendingChestController = __instance;
                Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
                MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Host Skip — waiting for clients to finish treasure");
                return false; // Block until all clients done
            }
        }

        return true;
    }

    /// <summary>
    /// BattleUpgradeCanvas.AcceptRelic — on client during treasure, send completion event.
    /// Also tracks chosen relic during post-battle boss/rare relic selection.
    /// </summary>
    [HarmonyPatch(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "AcceptRelic")]
    [HarmonyPostfix]
    public static void BattleUpgradeCanvas_AcceptRelic_Postfix(Relics.Relic relic)
    {
        if (!ShouldSuppressClientLogic) return;

        // Track boss/rare relic choice during post-battle native reward phase
        if (AllowNativeRewardLogic)
        {
            ClientChosenPostBattleRelicEffect = (int)relic.effect;
            ClientChosenPostBattleRelicName = relic.locKey;
            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatch] Client chose post-battle relic: '{relic.locKey}' (effect={relic.effect})");
        }

        // Treasure-specific flow
        if (!AllowTreasureLogic) return;
        if (Events.Handlers.Coop.CoopRewardState.ClientTreasureChoiceSent) return;

        try
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
            {
                sender.Send(new Events.Network.Scenarios.TreasureCompleteEvent
                {
                    ChosenRelicEffect = (int)relic.effect,
                    ChosenRelicName = relic.locKey,
                });
                MultiplayerPlugin.Logger?.LogInfo($"[ClientPatch] Client accepted treasure relic '{relic.locKey}' — sent TreasureCompleteEvent");
            }
            Events.Handlers.Coop.CoopRewardState.ClientTreasureChoiceSent = true;
            // Show the waiting overlay once this player is done. AcceptRelic only
            // closes the in-scene relic panel; the chest scene stays until every
            // client finishes, so without this the client sits on the scene with
            // no feedback.
            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
            Events.Handlers.Coop.CoopRewardState.TreasurePhaseActive = true;
            Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = false;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[ClientPatch] Failed to send TreasureCompleteEvent: {ex.Message}");
        }
    }

    // =========================================================================
    // HOST: CAPTURE BOSS/RARE RELIC CHOICES — send to clients
    // CLIENT: REPLACE RELICS WITH HOST CHOICES
    // =========================================================================

    /// <summary>
    /// BattleUpgradeCanvas.SetupRelicGrant — after the relic panel is configured:
    /// HOST: capture the displayed relics and send to clients.
    /// CLIENT: replace with host-provided relics if available.
    /// </summary>
    [HarmonyPatch(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "SetupRelicGrant")]
    [HarmonyPostfix]
    public static void BattleUpgradeCanvas_SetupRelicGrant_Postfix(
        PeglinUI.PostBattle.BattleUpgradeCanvas __instance)
    {
        if (!UI.LobbyUI.GameStartReceived) return;

        // During treasure scenes, both host and client pick relics natively from the
        // chest UI. Do NOT capture/replace relics — that's only for post-battle rewards.
        if (AllowTreasureLogic) return;
        if (IsHosting && Events.Handlers.Coop.CoopRewardState.TreasurePhaseActive) return;

        try
        {
            var relicPanelField = HarmonyLib.AccessTools.Field(
                typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_relicPanel");
            var relicPanel = relicPanelField?.GetValue(__instance) as UnityEngine.GameObject;
            if (relicPanel == null) return;

            var icons = relicPanel.GetComponentsInChildren<RelicIcon>(true);
            if (icons == null || icons.Length == 0) return;

            if (IsHosting)
            {
                // HOST: capture relics and dispatch to clients
                var evt = new Events.Network.Coop.PostBattleRelicChoicesEvent();
                foreach (var icon in icons)
                {
                    if (icon.relic != null && icon.transform.parent.gameObject.activeSelf)
                    {
                        evt.Choices.Add(new Events.Network.Coop.RelicChoiceEntry
                        {
                            Effect = (int)icon.relic.effect,
                            LocKey = icon.relic.locKey,
                        });
                    }
                }

                if (evt.Choices.Count > 0)
                {
                    var services = MultiplayerPlugin.Services;
                    if (services?.TryResolve<Events.IGameEventRegistry>(out var reg) == true)
                    {
                        reg.Dispatch(evt);
                        MultiplayerPlugin.Logger?.LogInfo(
                            $"[ClientPatches] Host captured {evt.Choices.Count} post-battle relic choices — sent to clients");
                    }
                }
            }
            else if (ShouldSuppressClientLogic)
            {
                // CLIENT: replace with host relics if available
                var choices = Events.Handlers.Coop.CoopRewardState.PendingPostBattleRelicChoices;
                if (choices != null && choices.Count > 0)
                {
                    var allRelics = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.Relic>();

                    for (int i = 0; i < icons.Length; i++)
                    {
                        if (i < choices.Count)
                        {
                            var choice = choices[i];
                            Relics.Relic found = null;
                            foreach (var r in allRelics)
                            {
                                if ((int)r.effect == choice.Effect)
                                {
                                    found = r;
                                    break;
                                }
                            }
                            if (found != null)
                            {
                                icons[i].SetRelic(found);
                                icons[i].shouldShowTooltip = false;
                                icons[i].transform.parent.gameObject.SetActive(true);
                            }
                        }
                        else
                        {
                            icons[i].transform.parent.gameObject.SetActive(false);
                        }
                    }

                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[ClientPatches] Client replaced relic panel with {choices.Count} host-provided choices");
                }
                else
                {
                    MultiplayerPlugin.Logger?.LogWarning(
                        "[ClientPatches] Client SetupRelicGrant — no host relic choices available yet");
                }
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[ClientPatches] SetupRelicGrant postfix failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // =========================================================================
    // Status effect UI suppression during non-host turns in coop
    // =========================================================================

    /// <summary>
    /// Suppress status effect UI updates on the host when a non-host player's
    /// state is active. This prevents the host's Peglin from visually gaining
    /// Ballusion (or other status effects) from another player's relics during
    /// their turn.
    /// </summary>
    [HarmonyPatch(typeof(Battle.StatusEffects.StatusEffectIconManager), nameof(Battle.StatusEffects.StatusEffectIconManager.UpdateStatusEffects))]
    [HarmonyPrefix]
    public static bool StatusEffectIconManager_UpdateStatusEffects_Prefix()
    {
        if (!GameState.CoopStateManager.SuppressStatusEffectUI) return true;
        return false; // Skip the UI update — effects are still in the list for gameplay
    }

    // =========================================================================
    // Block client-side bomb throwing (THROW_BOMBS state machine)
    // =========================================================================

    private static readonly System.Reflection.FieldInfo _bombsRegularField =
        AccessTools.Field(typeof(BattleController), "_bombsToThrowRegular");
    private static readonly System.Reflection.FieldInfo _bombsRiggedField =
        AccessTools.Field(typeof(BattleController), "_bombsToThrowRigged");

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
        if (!ShouldSuppressClientLogic) return true;

        _bombsRegularField?.SetValue(__instance, 0);
        _bombsRiggedField?.SetValue(__instance, 0);

        __result = EmptyCoroutine();
        return false;
    }

    private static IEnumerator EmptyCoroutine()
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
    private static System.Collections.Generic.HashSet<int> GetOwnedEffects(Relics.RelicManager rm)
    {
        var owned = new System.Collections.Generic.HashSet<int>();
        if (rm == null) return owned;
        try
        {
            var ownedField = AccessTools.Field(typeof(Relics.RelicManager), "_ownedRelics");
            var dict = ownedField?.GetValue(rm)
                as System.Collections.Generic.Dictionary<Relics.RelicEffect, Relics.Relic>;
            if (dict != null)
            {
                foreach (var k in dict.Keys) owned.Add((int)k);
            }
        }
        catch { }
        return owned;
    }

    /// <summary>Pick a random Relic prefab matching <paramref name="rarity"/> that isn't already owned.</summary>
    private static Relics.Relic PickRandomLocalRelic(Relics.RelicRarity rarity, Relics.RelicManager rm)
    {
        try
        {
            var owned = GetOwnedEffects(rm);
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.Relic>();
            // Relic is a ScriptableObject, so all returned are assets (no scene filtering needed).
            var candidates = new System.Collections.Generic.List<Relics.Relic>();
            foreach (var r in all)
            {
                if (r == null) continue;
                if (r.globalRarity != rarity) continue;
                if (owned.Contains((int)r.effect)) continue;
                candidates.Add(r);
            }
            if (candidates.Count == 0)
            {
                // Fallback: allow any rarity if pool empty
                foreach (var r in all)
                {
                    if (r == null) continue;
                    if (owned.Contains((int)r.effect)) continue;
                    candidates.Add(r);
                }
            }
            if (candidates.Count == 0) return null;
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] PickRandomLocalRelic failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// CLIENT-ONLY prefix: when the native Treasure UI asks BattleUpgradeCanvas
    /// to set up a relic grant, roll a single local random relic and display only
    /// that one icon. Prevents the "5 bugged Blastic Powder" artifact from broken
    /// client-side relic queues.
    /// </summary>
    [HarmonyPatch(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "SetupRelicGrant")]
    [HarmonyPrefix]
    public static bool BattleUpgradeCanvas_SetupRelicGrant_ClientOverride_Prefix(
        PeglinUI.PostBattle.BattleUpgradeCanvas __instance,
        Relics.RelicRarity rarity,
        bool isTreasure)
    {
        // Only intercept on client AND only when the client is driving treasure UI themselves
        if (!ShouldSuppressClientLogic) return true;
        if (!AllowTreasureLogic) return true;

        try
        {
            var mainOptionsField = AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_mainOptionsPanel");
            var relicPanelField = AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_relicPanel");
            var stateField = AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_state");
            var rarityField = AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_relicGrantRarity");
            var relicManagerField = AccessTools.Field(typeof(PeglinUI.PostBattle.BattleUpgradeCanvas), "_relicManager");

            var mainOptions = mainOptionsField?.GetValue(__instance) as GameObject;
            var relicPanel = relicPanelField?.GetValue(__instance) as GameObject;
            var relicManager = relicManagerField?.GetValue(__instance) as Relics.RelicManager;
            if (relicPanel == null) return true; // fall through

            mainOptions?.SetActive(false);
            relicPanel.SetActive(true);

            if (stateField != null && stateField.FieldType.IsEnum)
            {
                try { stateField.SetValue(__instance, System.Enum.Parse(stateField.FieldType, "RELIC")); }
                catch { }
            }
            rarityField?.SetValue(__instance, rarity);

            var relic = PickRandomLocalRelic(rarity, relicManager);
            var icons = relicPanel.GetComponentsInChildren<RelicIcon>(true);
            for (int i = 0; i < icons.Length; i++)
            {
                if (i == 0 && relic != null)
                {
                    icons[i].SetRelic(relic);
                    icons[i].shouldShowTooltip = false;
                    icons[i].transform.parent.gameObject.SetActive(true);
                }
                else
                {
                    icons[i].transform.parent.gameObject.SetActive(false);
                }
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatch] Treasure relic grant overridden to single local relic " +
                $"'{relic?.locKey ?? "<none>"}' rarity={rarity} (isTreasure={isTreasure})");
            return false; // Skip original
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[ClientPatch] SetupRelicGrant client override failed, falling through: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// CLIENT-ONLY prefix: when TextScenarioInteractions.OfferRelic is called,
    /// replace the seeded-queue logic with a single local random relic. Same
    /// reason as above — the client's queues are broken.
    /// </summary>
    [HarmonyPatch(typeof(Scenarios.TextScenarioInteractions), "OfferRelic")]
    [HarmonyPrefix]
    public static bool TextScenarioInteractions_OfferRelic_ClientOverride_Prefix(
        Scenarios.TextScenarioInteractions __instance,
        Relics.RelicRarity rarity)
    {
        if (!ShouldSuppressClientLogic) return true;
        if (!AllowTextScenarioLogic) return true;

        try
        {
            var relicPanelField = AccessTools.Field(typeof(Scenarios.TextScenarioInteractions), "relicPanel");
            var skipButtonField = AccessTools.Field(typeof(Scenarios.TextScenarioInteractions), "skipRelicButton");
            var rmField = AccessTools.Field(typeof(Scenarios.TextScenarioInteractions), "relicManager");

            var relicPanel = relicPanelField?.GetValue(__instance) as GameObject;
            var skipButton = skipButtonField?.GetValue(__instance) as UnityEngine.UI.Button;
            var relicManager = rmField?.GetValue(__instance) as Relics.RelicManager;
            if (relicPanel == null) return true;

            relicPanel.SetActive(true);

            var relic = PickRandomLocalRelic(rarity, relicManager);
            var icons = relicPanel.GetComponentsInChildren<RelicIcon>(true);
            for (int i = 0; i < icons.Length; i++)
            {
                if (i == 0 && relic != null)
                {
                    icons[i].SetRelic(relic);
                    icons[i].transform.parent.gameObject.SetActive(true);
                }
                else
                {
                    icons[i].transform.parent.gameObject.SetActive(false);
                }
            }
            skipButton?.gameObject.SetActive(true);

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatch] TextScenario OfferRelic overridden to single local relic " +
                $"'{relic?.locKey ?? "<none>"}' rarity={rarity}");
            return false;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[ClientPatch] OfferRelic client override failed, falling through: {ex.Message}");
            return true;
        }
    }

    // --- SlimeBoss: block client-side pachinkoBallSpawnLocation mutation ---
    // SlimeBoss.UpdatePachinkoPos() alternates the ball spawn location each turn
    // by incrementing a local counter. On the client this runs independently of
    // the host, so the two sides drift out of sync (host spawns left, client spawns
    // right, etc.). The heartbeat EnemyStateSnapshot carries the host's authoritative
    // pachinkoBallSpawnLocation — block the client-side mutation so only the
    // heartbeat applier writes to it.

    [HarmonyPatch(typeof(Battle.SlimeBoss), "UpdatePachinkoPos")]
    [HarmonyPrefix]
    public static bool SlimeBoss_UpdatePachinkoPos_Prefix()
        => !ShouldSuppressClientLogic;

    // --- SteamManager: skip Steam init in dev-multi to allow multiple instances ---

    [HarmonyPatch(typeof(SteamManager), "Awake")]
    [HarmonyPrefix]
    public static bool SteamManager_Awake_Prefix(SteamManager __instance)
    {
        if (System.Environment.GetEnvironmentVariable("SKIP_STEAM_INIT") != "1")
            return true;

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] SKIP_STEAM_INIT set, skipping SteamManager.Awake");

        // Set the singleton so Initialized returns false without creating new GameObjects
        var field = typeof(SteamManager).GetField("s_instance",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(null, __instance);

        UnityEngine.Object.DontDestroyOnLoad(__instance.gameObject);
        return false;
    }
}
