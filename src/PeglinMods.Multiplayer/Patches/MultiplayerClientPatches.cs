using System;
using Battle;
using Data;
using HarmonyLib;
using I2.Loc;
using Loading;
using Map;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.Events.Network.Map;
using PeglinMods.Multiplayer.Events.Subscriptions;
using PeglinMods.Multiplayer.Multiplayer;
using Tutorial;
using UnityEngine;
using Worldmap;
using Random = UnityEngine.Random;

namespace PeglinMods.Multiplayer.Patches;

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

    [HarmonyPatch(typeof(BattleController), "Update")]
    [HarmonyPrefix]
    public static bool BattleController_Update_Prefix(BattleController __instance)
    {
        if (!ShouldSuppressClientLogic) return true;

        // In co-op: handle aiming input ourselves instead of running BattleController.Update.
        // BattleController.Update in AWAITING_SHOT fires OnStartedAwaitingShot, increments
        // round count, resets per-shot relics, etc. — side effects that corrupt client state.
        // Instead, we read mouse position, update the aimer visual, and send a ShootRequest
        // to the host when the client clicks.
        if (Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn && !ClientShotSentThisTurn)
        {
            HandleClientAiming(__instance);
        }
        else
        {
            // Not aiming — clean up client ball
            if (_clientBallInitialized)
            {
                if (_clientBallGO != null)
                {
                    UnityEngine.Object.Destroy(_clientBallGO);
                    _clientBallGO = null;
                }
                _clientBallInitialized = false;
            }
        }

        return false; // Always block BattleController.Update on the client
    }

    /// <summary>
    /// Handles client-side aiming input during the client's turn.
    /// Reads mouse position, updates the ClientBallRenderer aim direction,
    /// and sends a ShootRequestEvent to the host when the player clicks.
    /// </summary>
    // Track whether we've initialized the ball for client aiming this turn
    private static bool _clientBallInitialized;

    // The client-created PachinkoBall for aiming during client's turn
    private static UnityEngine.GameObject _clientBallGO;

    /// <summary>
    /// Create a real PachinkoBall on the client from the deck prefab, just like
    /// BattleController.DrawBall does. Set it to AIMING state and let its native
    /// Update() handle everything: mouse input, rotation, TrajectorySimulation.
    /// When the player clicks, Fire() is intercepted by PachinkoBall_Fire_Prefix.
    /// </summary>
    private static void HandleClientAiming(BattleController bc)
    {
        if (!_clientBallInitialized)
        {
            // Destroy previous client ball if any
            if (_clientBallGO != null)
            {
                UnityEngine.Object.Destroy(_clientBallGO);
                _clientBallGO = null;
            }

            // Only try once per turn — don't spam on failure
            _clientBallInitialized = true;

            // Get spawn position
            var spawnPos = bc.pachinkoBallSpawnLocation;
            if (spawnPos == UnityEngine.Vector2.zero)
            {
                var player = UnityEngine.GameObject.FindGameObjectWithTag("Player");
                if (player != null) spawnPos = (UnityEngine.Vector2)player.transform.position;
            }

            // Get the orb prefab from the deck — same as what DeckManager.DrawBall returns
            try
            {
                var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
                var dm = dms.Length > 0 ? dms[0] : null;
                if (dm == null) { MultiplayerPlugin.Logger?.LogWarning("[ClientAim] No DeckManager"); return; }

                // Peek at the top of shuffled deck to get the prefab
                var shuffledField = HarmonyLib.AccessTools.Field(typeof(DeckManager), "shuffledDeck");
                var shuffled = shuffledField?.GetValue(dm) as System.Collections.Generic.Stack<UnityEngine.GameObject>;
                UnityEngine.GameObject prefab = null;
                if (shuffled != null && shuffled.Count > 0)
                    prefab = shuffled.Peek();

                // Fallback: use any orb from completeDeck
                if (prefab == null && DeckManager.completeDeck != null && DeckManager.completeDeck.Count > 0)
                    prefab = DeckManager.completeDeck[0];

                if (prefab == null) { MultiplayerPlugin.Logger?.LogWarning("[ClientAim] No orb prefab found"); return; }

                // Instantiate the ball at the spawn point — same as BattleController.DrawBall
                _clientBallGO = UnityEngine.Object.Instantiate(prefab, spawnPos, UnityEngine.Quaternion.identity);

                var ball = _clientBallGO.GetComponent<PachinkoBall>();
                if (ball == null) { MultiplayerPlugin.Logger?.LogWarning("[ClientAim] Instantiated prefab has no PachinkoBall"); return; }

                // Get managers from scene for Init
                var rms = Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                var rm = rms.Length > 0 ? rms[0] : null;

                // Get PredictionManager from BattleController — needed for trajectory rendering
                PredictionManager predMgr = null;
                try
                {
                    var predField = HarmonyLib.AccessTools.Field(typeof(Battle.BattleController), "_predictionManager");
                    predMgr = predField?.GetValue(bc) as PredictionManager;
                }
                catch { }

                // Get PlayerStatusEffectController from player transform
                Battle.StatusEffects.PlayerStatusEffectController psec = null;
                try
                {
                    var playerGO = UnityEngine.GameObject.FindGameObjectWithTag("Player");
                    if (playerGO != null)
                        psec = playerGO.GetComponentInChildren<Battle.StatusEffects.PlayerStatusEffectController>();
                }
                catch { }

                ball.Init(rm, dm, UnityEngine.Vector2.down, predMgr, psec);
                ball.InitializeMembers();
                ball.IsDummy = true; // Dummy so it doesn't process peg collisions on client

                // Set AIMING state manually — Arm() NREs because _predictionManager is null
                var stateProp = HarmonyLib.AccessTools.Property(typeof(PachinkoBall), "CurrentState");
                stateProp?.GetSetMethod(true)?.Invoke(ball, new object[] { PachinkoBall.FireballState.AIMING });

                // Set trajectory radius from collider
                try { ball.SetTrajectorySimulationRadius(); } catch { }

                // Enable trajectory simulation
                var trajSim = _clientBallGO.GetComponent<TrajectorySimulation>();
                if (trajSim != null)
                {
                    trajSim.playerFire = _clientBallGO;
                    trajSim.enabled = true;
                }

                // Disable physics so the ball doesn't fall
                var rb = _clientBallGO.GetComponent<UnityEngine.Rigidbody2D>();
                if (rb != null) rb.simulated = false;

                MultiplayerPlugin.Logger?.LogInfo(
                    $"[ClientAim] Created aiming ball at ({spawnPos.x:F1},{spawnPos.y:F1}), " +
                    $"prefab={prefab.name}, state={ball.CurrentState}, " +
                    $"trajSim={trajSim != null}, sightLine={trajSim?.sightLine != null}");
            }
            catch (System.Exception ex)
            {
                MultiplayerPlugin.Logger?.LogError($"[ClientAim] Failed to create ball: {ex}");
            }
        }

        // PachinkoBall.Update() silently fails due to internal NREs.
        // Drive aiming manually: read mouse, update ball rotation, call simulatePath().
        if (_clientBallGO == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        var mouseScreen = Input.mousePosition;
        mouseScreen.z = -cam.transform.position.z;
        var mouseWorld = cam.ScreenToWorldPoint(mouseScreen);
        var ballPos = _clientBallGO.transform.position;
        var aimDir = ((UnityEngine.Vector2)(mouseWorld - ballPos)).normalized;

        _clientBallGO.transform.right = aimDir;

        // Call simulatePath() on TrajectorySimulation via reflection to draw trajectory
        var ts = _clientBallGO.GetComponent<TrajectorySimulation>();
        if (ts != null && ts.enabled)
        {
            try
            {
                var simMethod = HarmonyLib.AccessTools.Method(typeof(TrajectorySimulation), "simulatePath");
                simMethod?.Invoke(ts, null);
            }
            catch { }
        }

        // On click, send shoot request
        if (Input.GetMouseButtonDown(0))
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
            {
                sender.Send(new Events.Network.Coop.ShootRequestEvent
                {
                    AimDirectionX = aimDir.x,
                    AimDirectionY = aimDir.y,
                });
                ClientShotSentThisTurn = true;
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[ClientAim] Sent ShootRequest: aim=({aimDir.x:F2},{aimDir.y:F2})");
            }
        }
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

        // Only fire when BattleController is in AWAITING_SHOT and there's a pending shot
        if (BattleController.CurrentBattleState != BattleController.BattleState.AWAITING_SHOT) return;

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

            // Use the real PachinkoBall.Fire() so all internal state (collision layers,
            // wall bounce tracking, shot timeout, etc.) is set up correctly.
            // ExecutingPendingShot bypasses PachinkoBall_Fire_Prefix's block.
            ExecutingPendingShot = true;
            try
            {
                activeBall.Fire();
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[ClientPatches] PachinkoBall.Fire() succeeded, aim=({aimVec.x:F2},{aimVec.y:F2}), " +
                    $"pos=({activeBallGO.transform.position.x:F1},{activeBallGO.transform.position.y:F1}), " +
                    $"isDummy={activeBall.IsDummy}, scale=({activeBallGO.transform.localScale.x:F2}), " +
                    $"layer={LayerMask.LayerToName(activeBallGO.layer)}");
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

        var gameStartEvent = UI.LobbyUI.LatestGameStartEvent;
        if (gameStartEvent?.FinalPlayers != null)
        {
            foreach (var player in gameStartEvent.FinalPlayers)
            {
                coopState.InitializePlayer(player.SlotIndex, player.ChosenClass, player.PlayerName);
                MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Initialized coop player: slot={player.SlotIndex}, name={player.PlayerName}, class={player.ChosenClass}");
            }

            // Capture host's initial state (slot 0) after GameInit has set up deck/relics/health
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
            foreach (var player in gameStartEvent.FinalPlayers)
            {
                if (player.IsHost) continue;

                var playerState = coopState.GetPlayerState(player.SlotIndex);
                if (playerState == null) continue;

                // All players start with the same max HP as the host
                float maxHp = hostState?.MaxHealth ?? (__instance.maxPlayerHealth?.Value ?? 0);
                playerState.CurrentHealth = maxHp; // Full health at start
                playerState.MaxHealth = maxHp;

                // Build starting deck from ClassLoadoutData
                var classLoadouts = StaticGameData.classLoadouts;
                Peglin.ClassSystem.ClassLoadoutData loadout = null;
                if (classLoadouts != null)
                {
                    foreach (var pair in classLoadouts)
                    {
                        if (pair.Class == (Peglin.ClassSystem.Class)player.ChosenClass)
                        { loadout = pair.Loadout; break; }
                    }
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
                                    clientRelicMgrs[0].AddRelic(relic);
                                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Client: added starting class relic {relic.effect} ({relic.locKey})");
                                }
                                catch { }
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
    /// </summary>
    [HarmonyPatch(typeof(PegManager), "ShuffleSpecialPegs")]
    [HarmonyPrefix]
    public static bool PegManager_ShuffleSpecialPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked ShuffleSpecialPegs — host will send peg types");
        return false;
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

    /// <summary>Block crit peg shuffling on client.</summary>
    [HarmonyPatch(typeof(PegManager), "ShuffleCritPegs")]
    [HarmonyPrefix]
    public static bool PegManager_ShuffleCritPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

    /// <summary>Block refresh peg creation on client.</summary>
    [HarmonyPatch(typeof(PegManager), "CreateRefreshPegs")]
    [HarmonyPrefix]
    public static bool PegManager_CreateRefreshPegs_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        return false;
    }

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
    public static Exception MapController_Start_Finalizer(Exception __exception)
    {
        // HOST: send fresh map sync with real node types
        if (IsHosting)
        {
            try
            {
                if (MultiplayerPlugin.Services?.TryResolve<GameState.IGameStateSyncService>(out var sync) == true)
                {
                    sync.SyncMap();
                    MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Host MapController.Start done — sent immediate map sync with node types");
                }
            }
            catch { }
            return __exception;
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

    // ShuffleCompleteDeck is NOT blocked — it triggers onDeckShuffled which
    // triggers StartShuffleAnimation which populates _displayOrbs for the
    // deck UI. The client shuffles with wrong RNG order, but DeckApplier
    // overwrites shuffledDeck with the correct host order immediately after.
    // Without this, the entire deck UI is dead (no orbs visible).

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

        registry.Dispatch(new PeglinMods.Multiplayer.Events.Network.Battle.DamageTextEvent
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
    /// </summary>
    [HarmonyPatch(typeof(RegularPeg), "ConvertPegToType")]
    [HarmonyPostfix]
    public static void RegularPeg_ConvertPegToType_Postfix(RegularPeg __instance, Peg.PegType type, GameObject __result)
    {
        if (type != Peg.PegType.BOMB || __result == null || __result == __instance.gameObject) return;
        if (MultiplayerPlugin.Services == null) return;
        if (!MultiplayerPlugin.Services.TryResolve<PeglinMods.Multiplayer.Utility.PegIdentifier>(out var pegId)) return;

        var oldGuid = pegId.GetGuid(__instance);
        if (string.IsNullOrEmpty(oldGuid)) return;

        var newBomb = __result.GetComponent<Peg>();
        if (newBomb != null)
        {
            pegId.Register(newBomb, oldGuid);
        }
    }

    // =========================================================================
    // ATTACK ANIMATION DATA — capture attack trigger and target for sync
    // =========================================================================

    /// <summary>Stores the last attack animation trigger for the AttackStartedEvent.</summary>
    internal static string LastAttackAnimTrigger;
    internal static string LastAttackTargetGuid;

    /// <summary>Capture attack trigger and target enemy when attack starts.</summary>
    [HarmonyPatch(typeof(Battle.Attacks.AttackManager), "Attack")]
    [HarmonyPostfix]
    public static void AttackManager_Attack_Postfix(Battle.Attacks.AttackManager __instance, Battle.Enemies.Enemy target)
    {
        if (!IsHosting) return;
        try
        {
            var attackField = HarmonyLib.AccessTools.Field(typeof(Battle.Attacks.AttackManager), "_attack");
            var attack = attackField?.GetValue(__instance) as Battle.Attacks.Attack;
            LastAttackAnimTrigger = attack?.PeglinAttackAnimationTrigger ?? "attack";

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
        if (!MultiplayerPlugin.Services.TryResolve<PeglinMods.Multiplayer.Utility.EnemyIdentifier>(out var enemyId)) return;

        var guid = enemyId.GetGuid(enemy);
        if (string.IsNullOrEmpty(guid)) return;

        registry.Dispatch(new PeglinMods.Multiplayer.Events.Network.Battle.AnimationSyncEvent
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
        if (!MultiplayerPlugin.Services.TryResolve<PeglinMods.Multiplayer.Utility.EnemyIdentifier>(out var enemyId)) return;

        var guid = enemyId.GetGuid(enemy);
        if (string.IsNullOrEmpty(guid)) return;

        registry.Dispatch(new PeglinMods.Multiplayer.Events.Network.Battle.AnimationSyncEvent
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
    public static void MapController_Awake_Prefix()
    {
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
        if (ShouldSuppressClientLogic)
        {
            if (UI.LobbyUI.GameStartReceived
                && Events.Handlers.Coop.TurnChangeClientHandler.IsMyTurn
                && !ClientShotSentThisTurn)
            {
                var aimVec = __instance.aimVector;
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
                {
                    sender.Send(new Events.Network.Coop.ShootRequestEvent
                    {
                        AimDirectionX = aimVec.x,
                        AimDirectionY = aimVec.y,
                    });
                    ClientShotSentThisTurn = true;
                    MultiplayerPlugin.Logger?.LogInfo(
                        $"[ClientPatches] Fire intercepted → ShootRequest: aim=({aimVec.x:F2},{aimVec.y:F2})");
                }
            }
            return false;
        }

        return true; // Non-multiplayer: allow
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
        registry.Dispatch(new NodeActivatedEvent
        {
            PosX = pos.x,
            PosY = pos.y,
            BattleName = battleName,
            RngState = SerializeRandomState(Random.state),
        });
        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Host activated node at ({pos.x:F1}, {pos.y:F1}), battle={battleName}");
    }

    // =========================================================================
    // HOST: MULTIBALL SYNC — send additional ball spawns to client
    // =========================================================================

    /// <summary>
    /// When the host spawns a multiball, send its position and velocity to client
    /// so it can render the additional ball visually.
    /// </summary>
    [HarmonyPatch(typeof(PachinkoBall), "SpawnMultiballFromLocation")]
    [HarmonyPostfix]
    public static void PachinkoBall_SpawnMultiballFromLocation_Postfix(GameObject __result)
    {
        if (!IsHosting || __result == null) return;

        try
        {
            var rb = __result.GetComponent<UnityEngine.Rigidbody2D>();
            var pos = __result.transform.position;
            var vel = rb != null ? rb.velocity : UnityEngine.Vector2.zero;

            string orbName = null;
            var atk = __result.GetComponent<Battle.Attacks.Attack>();
            if (atk != null) orbName = atk.gameObject.name;

            var registry = MultiplayerPlugin.Services?.Resolve<Events.IGameEventRegistry>();
            registry?.Dispatch(new Events.Network.Ball.MultiballSpawnedEvent
            {
                PosX = pos.x,
                PosY = pos.y,
                VelX = vel.x,
                VelY = vel.y,
                OrbName = orbName,
            });

            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Host spawned multiball at ({pos.x:F1},{pos.y:F1}) vel=({vel.x:F1},{vel.y:F1})");
        }
        catch { }
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
    }
}
