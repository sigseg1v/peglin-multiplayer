using System.Collections.Generic;
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
    // Track ShopManager instances we've already spawned shop blocks for so a
    // second NavigatePhaseStart in the same scene doesn't double-spawn.
    private static readonly HashSet<int> _spawnedShopBlockIds = new();

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
        CoopRewardState.TreasureAwaitingHostNavigation = false;
        CoopRewardState.TextScenarioAwaitingHostNavigation = false;
        CoopRewardState.ShopAwaitingHostNavigation = false;
        CoopRewardState.PegMinigameAwaitingHostNavigation = false;
        CoopRewardState.TreasurePhaseActive = false;
        CoopRewardState.TextScenarioPhaseActive = false;
        CoopRewardState.ShopPhaseActive = false;
        CoopRewardState.PegMinigamePhaseActive = false;
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
            if (networkEvent.Source == "nav_only")
            {
                InvokeNavOnlyArm(log, networkEvent.ChildNodeCount);
            }
            else
            {
                InvokePostBattleStartNavigation(log, networkEvent.ChildNodeCount);
            }
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

    /// <summary>
    /// Client-side: arm a nav ball using the local NavOnlyController. Used by
    /// nav_only phases (treasure / shop / text_scenario / peg_minigame). The
    /// native NavOnlyController.PrepareForNavigation can't run here because it
    /// reads StaticGameData.currentNode (null on clients) and falls through to
    /// LoadSelectedScene — so we replicate the safe parts manually using the
    /// host-authoritative ChildNodeCount.
    /// </summary>
    private static void InvokeNavOnlyArm(BepInEx.Logging.ManualLogSource log, int childNodeCount)
    {
        var nocs = Resources.FindObjectsOfTypeAll<global::NavOnlyController>();
        global::NavOnlyController target = null;
        if (nocs != null)
        {
            foreach (var n in nocs)
            {
                if (n == null)
                {
                    continue;
                }

                if (n.gameObject != null && n.gameObject.scene.IsValid())
                {
                    target = n;
                    if (n.gameObject.activeInHierarchy)
                    {
                        break;
                    }
                }
            }
        }

        if (target == null)
        {
            log?.LogWarning("[CoopNavigate] Client: no NavOnlyController found to arm");
            return;
        }

        log?.LogInfo(
            $"[CoopNavigate] Found NavOnlyController '{target.name}' (active={target.gameObject.activeInHierarchy}, treasure={target.isTreasureScene}) — arming (children={childNodeCount})");

        if (!target.gameObject.activeInHierarchy)
        {
            target.gameObject.SetActive(true);
        }

        // Dismiss whichever scenario UI is still covering the playfield.
        // The native CloseStore / Skip / ConversationEnded paths that would
        // hide the shopUI, fade chest UI elements, or close the dialogue
        // canvas were all blocked on the client (we sent a Complete event
        // and returned false). Without this dismiss step the player sees the
        // shop / chest / dialogue UI on top of the freshly-armed nav ball
        // and they can't aim — the symptom for shop was "clicked Exit Store
        // and went right back into the store". Camera also gets repositioned
        // to the nav Y the host's tween would have driven to.
        DismissActiveScenarioForNavigation(target, log);

        // Hide the dialogue/event/relic UI overlay if the scenario controller
        // didn't dismiss it — we're entering nav phase regardless.
        TryFadeOutNavCurtain(log);

        // Reflect _ball; if the scenario's Initialize never ran (rare path),
        // call it now to spawn the orb at the configured start transform.
        var ballField = AccessTools.Field(typeof(global::NavOnlyController), "_ball");
        var ball = ballField?.GetValue(target) as GameObject;
        if (ball == null)
        {
            log?.LogInfo("[CoopNavigate] Client: NavOnlyController._ball is null — calling Initialize()");
            try
            {
                target.Initialize();
                ball = ballField?.GetValue(target) as GameObject;
            }
            catch (System.Exception ex)
            {
                log?.LogError($"[CoopNavigate] Client: NavOnlyController.Initialize() threw: {ex.Message}");
            }
        }

        if (ball == null)
        {
            log?.LogError("[CoopNavigate] Client: nav ball still null after Initialize — cannot arm");
            return;
        }

        var pb = ball.GetComponent<PachinkoBall>();
        if (pb == null)
        {
            log?.LogError("[CoopNavigate] Client: nav ball has no PachinkoBall component");
            return;
        }

        try
        {
            pb.Arm();
        }
        catch (System.Exception ex)
        {
            log?.LogError($"[CoopNavigate] Client: PachinkoBall.Arm threw: {ex.Message}");
        }

        // Defensive: Arm() only transitions WAITING -> AIMING. If the ball was
        // somehow left in another state (e.g. the prediction manager was null
        // when Arm ran), force the state directly so PachinkoBall.LateUpdate's
        // AIMING branch (where mouse rotation + Fire() lives) actually runs.
        try
        {
            if (pb.CurrentState != PachinkoBall.FireballState.AIMING)
            {
                var stateProp = AccessTools.Property(typeof(PachinkoBall), "CurrentState");
                stateProp?.GetSetMethod(true)?.Invoke(pb, new object[] { PachinkoBall.FireballState.AIMING });
                log?.LogInfo($"[CoopNavigate] Forced ball state -> AIMING (was {pb.CurrentState})");
            }
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] Force-AIMING failed: {ex.Message}");
        }

        var traj = ball.GetComponent<TrajectorySimulation>();
        if (traj)
        {
            traj.enabled = true;
        }

        // PachinkoBall.AIMING in PredictionManager.Predict expects CopyAllPegs
        // to have run, otherwise the trajectory line renderer collides with
        // stale peg state and reports a flat empty path. PrepareForNavigation
        // does this on the host; mirror it here.
        try
        {
            var pm = Object.FindObjectOfType<global::PredictionManager>();
            pm?.CopyAllPegs();
            log?.LogInfo($"[CoopNavigate] Client called PredictionManager.CopyAllPegs (pm={(pm != null ? "ok" : "null")})");
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] CopyAllPegs failed: {ex.Message}");
        }

        ConfigureNavOnlySlotManagers(target, childNodeCount, log);

        log?.LogInfo("[CoopNavigate] Client armed NavOnly nav ball");
    }

    /// <summary>
    /// Activate left/center/right SlotManagers on NavOnlyController based on
    /// the host-authoritative child count. Mirrors the slot-config branches in
    /// NavOnlyController.PrepareForNavigation but uses a neutral icon/color
    /// since clients have no live MapNode tree.
    /// </summary>
    private static void ConfigureNavOnlySlotManagers(
        global::NavOnlyController noc,
        int childCount,
        BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            var leftField = AccessTools.Field(typeof(global::NavOnlyController), "_leftSlotManager");
            var rightField = AccessTools.Field(typeof(global::NavOnlyController), "_rightSlotManager");
            var centerField = AccessTools.Field(typeof(global::NavOnlyController), "_centreSlotManager");

            var left = leftField?.GetValue(noc) as global::Battle.SlotManager;
            var right = rightField?.GetValue(noc) as global::Battle.SlotManager;
            var center = centerField?.GetValue(noc) as global::Battle.SlotManager;

            if (left == null || right == null)
            {
                log?.LogWarning("[CoopNavigate] Client: NavOnlyController slot managers not wired");
                return;
            }

            var color = Color.white;
            Sprite icon = null;
            try
            {
                var anyNode = Resources.FindObjectsOfTypeAll<global::Worldmap.MapNode>();
                if (anyNode != null && anyNode.Length > 0)
                {
                    icon = anyNode[0].activeIcon;
                }
            }
            catch
            {
                // Best-effort icon lookup.
            }

            left.gameObject.SetActive(true);
            right.gameObject.SetActive(true);

            if (childCount <= 1)
            {
                left.ConfigureForNavigation(icon, color);
                right.ConfigureForNavigation(icon, color);
                left.ToggleNavigationIconVisibility(visible: false);
                right.ToggleNavigationIconVisibility(visible: false);
                if (center != null)
                {
                    center.gameObject.SetActive(true);
                    center.ConfigureForIconOnly(icon);
                }
            }
            else
            {
                left.ConfigureForNavigation(icon, color);
                right.ConfigureForNavigation(icon, color);
                if (center != null)
                {
                    center.gameObject.SetActive(true);
                    if (childCount > 2)
                    {
                        center.ConfigureForNavigation(icon, color);
                    }
                    else
                    {
                        center.ConfigureDudNavigation();
                    }
                }
            }

            log?.LogInfo($"[CoopNavigate] Client activated NavOnly slot managers (children={childCount})");
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] ConfigureNavOnlySlotManagers failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Find the scenario controller that owns this NavOnlyController and force
    /// its visual close-out. The host's CloseStore / Skip / NavigationDelay
    /// already animated shopUI / chest UI / dialogue canvas off-screen and
    /// panned the camera up to the nav playfield before broadcasting this
    /// event; the client never ran any of that because each scenario's exit
    /// hook returned false to gate on the wait-for-all. Snap the visuals into
    /// the post-transition state so the nav ball is actually aimable.
    /// </summary>
    private static void DismissActiveScenarioForNavigation(
        global::NavOnlyController noc,
        BepInEx.Logging.ManualLogSource log)
    {
        if (noc == null)
        {
            return;
        }

        try
        {
            // The scenario controllers reference navController via [SerializeField]
            // as a sibling, not as a parent — so GetComponentInParent returns null.
            // Find them globally and match by reference identity to noc.
            var shops = Resources.FindObjectsOfTypeAll<global::Scenarios.Shop.ShopManager>();
            foreach (var s in shops)
            {
                if (s == null || s.gameObject == null)
                {
                    continue;
                }

                if (s.gameObject.scene.IsValid() && s.navController == noc)
                {
                    DismissShopForNavigation(s, log);
                    return;
                }
            }

            var chests = Resources.FindObjectsOfTypeAll<global::Scenarios.ChestScenarioController>();
            foreach (var c in chests)
            {
                if (c == null || c.gameObject == null)
                {
                    continue;
                }

                if (c.gameObject.scene.IsValid() && c.navController == noc)
                {
                    DismissChestForNavigation(c, log);
                    return;
                }
            }

            var dialogues = Resources.FindObjectsOfTypeAll<global::RNG.Scenarios.DialogueSystemScenario>();
            foreach (var d in dialogues)
            {
                if (d == null || d.gameObject == null)
                {
                    continue;
                }

                if (!d.gameObject.scene.IsValid())
                {
                    continue;
                }

                var dnav = d.navController?.GetComponent<global::NavOnlyController>();
                if (dnav == noc)
                {
                    DismissDialogueForNavigation(d, log);
                    return;
                }
            }

            log?.LogWarning("[CoopNavigate] DismissActiveScenarioForNavigation: no matching scenario controller found for this NavOnlyController");
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] DismissActiveScenarioForNavigation failed: {ex.Message}");
        }
    }

    private static void DismissShopForNavigation(
        global::Scenarios.Shop.ShopManager shop,
        BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            if (shop.shopUI != null && shop.shopUI.gameObject.activeInHierarchy)
            {
                shop.shopUI.gameObject.SetActive(false);
            }

            if (shop.fadeCurtain != null)
            {
                shop.fadeCurtain.enabled = false;
                shop.fadeCurtain.raycastTarget = false;
            }

            // navCurtain is the black overlay that covers the playfield while
            // the shop is open. The multi-child StartNavigation path fades it
            // to alpha=0 then DisableCurtain disables the Image. CloseStore
            // never ran on the client, so without this the playfield (pegs,
            // bumpers, slot triggers, aimer) is hidden behind a solid black
            // image — and even after we hide the Image the GameObject's
            // GraphicRaycaster can still intercept clicks unless raycastTarget
            // is also false.
            if (shop.navCurtain != null)
            {
                var c = shop.navCurtain.color;
                shop.navCurtain.color = new Color(c.r, c.g, c.b, 0f);
                shop.navCurtain.enabled = false;
                shop.navCurtain.raycastTarget = false;
            }

            // DisableCurtain also activates the playfield mouse detector so the
            // player can aim by moving the cursor. Without this the trajectory
            // simulation has nothing to track against.
            if (shop.playfieldMouseDetector != null && !shop.playfieldMouseDetector.gameObject.activeSelf)
            {
                shop.playfieldMouseDetector.gameObject.SetActive(true);
            }

            // pegLayout holds the prediction pegboard for nav. Awake doesn't
            // disable it but defensive — make sure it's active.
            if (shop.pegLayout != null && !shop.pegLayout.activeSelf)
            {
                shop.pegLayout.SetActive(true);
            }

            // CloseStore's first line on the host is:
            //   _predictionManager.Initialize(pegLayout, _pegboardFrame, relicManager, navigation: true)
            // Without this, _predictionManager has no peg cache, so the
            // trajectory line renderer that tells the player where the nav
            // ball will go has nothing to render — the aimer is invisible
            // even though the ball is alive and PachinkoBall.Update is
            // rotating it with the cursor. This was the "no aimer" symptom.
            try
            {
                var pm = Object.FindObjectOfType<global::PredictionManager>();
                if (pm != null && shop.pegLayout != null && shop.relicManager != null)
                {
                    var pegFrameField = AccessTools.Field(typeof(global::Scenarios.Shop.ShopManager), "_pegboardFrame");
                    var pegFrame = pegFrameField?.GetValue(shop) as GameObject;
                    pm.Initialize(shop.pegLayout, pegFrame, shop.relicManager, navigation: true);
                    log?.LogInfo($"[CoopNavigate] Client initialized PredictionManager (pegLayout={shop.pegLayout.name}, frame={(pegFrame != null ? pegFrame.name : "null")})");
                }
                else
                {
                    log?.LogWarning($"[CoopNavigate] Client: cannot init PredictionManager (pm={(pm != null ? "ok" : "null")}, pegLayout={(shop.pegLayout != null ? "ok" : "null")}, relicMgr={(shop.relicManager != null ? "ok" : "null")})");
                }
            }
            catch (System.Exception ex)
            {
                log?.LogWarning($"[CoopNavigate] PredictionManager.Initialize failed: {ex.Message}");
            }

            // CloseStore spawns one PegBlock per shop orb on the orb row and
            // one per shop relic on the relic row (SpawnBlocksToMatchShopItems).
            // Without these the playfield only has the static layout pegs at
            // the top and the bottom looks empty. Run them now.
            try
            {
                if (shop.pegLayout != null && _spawnedShopBlockIds.Add(shop.GetInstanceID()))
                {
                    var spawner = shop.pegLayout.GetComponent<global::Scenarios.Shop.SpawnBlocksToMatchShopItems>();
                    if (spawner != null)
                    {
                        var orbsField = AccessTools.Field(typeof(global::Scenarios.Shop.ShopManager), "_purchasableOrbs");
                        var relicsField = AccessTools.Field(typeof(global::Scenarios.Shop.ShopManager), "_purchasableRelics");
                        var orbRemovedField = AccessTools.Field(typeof(global::Scenarios.Shop.ShopManager), "_orbRemoved");

                        var orbs = orbsField?.GetValue(shop) as global::Scenarios.Shop.IPurchasableItem[];
                        var relics = relicsField?.GetValue(shop) as global::Scenarios.Shop.IPurchasableItem[];
                        var orbRemoved = (bool?)orbRemovedField?.GetValue(shop) ?? false;

                        if (orbs != null)
                        {
                            spawner.SpawnOrbBlocks(orbs);
                        }

                        if (relics != null)
                        {
                            spawner.SpawnRelicBlocks(relics);
                        }

                        spawner.ToggleRemoveOrbRow(orbRemoved);
                        log?.LogInfo($"[CoopNavigate] Client spawned shop blocks: orbs={orbs?.Length ?? 0}, relics={relics?.Length ?? 0}, orbRemoved={orbRemoved}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                log?.LogWarning($"[CoopNavigate] SpawnBlocksToMatchShopItems failed: {ex.Message}");
            }

            // Camera was sitting at shop-Y; the host tweened to cameraNavY
            // during CloseStore. Snap to match so the playfield + nav ball
            // are framed identically to the host.
            if (Camera.main != null)
            {
                var pos = Camera.main.transform.position;
                Camera.main.transform.position = new UnityEngine.Vector3(pos.x, shop.cameraNavY, pos.z);
            }

            global::Scenarios.Shop.ShopManager.isOpen = false;
            global::PauseMenu.PauseBlock = false;

            // Mark the store closed so ShopManager.Update stops trying to claim
            // EventSystem focus for the shop's first selectable. exitStoreButton
            // also gets disabled to mirror the host's CloseStore.
            try
            {
                AccessTools.Field(typeof(global::Scenarios.Shop.ShopManager), "_storeClosed")
                    ?.SetValue(shop, true);
            }
            catch (System.Exception ex)
            {
                log?.LogWarning($"[CoopNavigate] set _storeClosed failed: {ex.Message}");
            }

            try
            {
                shop.exitStoreButton.interactable = false;
            }
            catch
            {
            }

            // No active selection; let the playfield receive mouse clicks.
            try
            {
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            }
            catch
            {
            }

            // Clear the wait-for-shop flags so CoopRewardUI doesn't redraw the
            // overlay over the nav playfield.
            CoopRewardState.ShopPhaseActive = false;
            CoopRewardState.ShopAwaitingHostNavigation = false;
            CoopRewardState.WaitingForOtherPlayers = false;

            log?.LogInfo("[CoopNavigate] Client dismissed ShopManager UI for navigation");
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] DismissShopForNavigation failed: {ex.Message}");
        }
    }

    private static void DismissChestForNavigation(
        global::Scenarios.ChestScenarioController chest,
        BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            if (chest.skipButton != null && chest.skipButton.gameObject.activeInHierarchy)
            {
                chest.skipButton.gameObject.SetActive(false);
            }

            if (chest.clickTextContainer != null && chest.clickTextContainer.activeInHierarchy)
            {
                chest.clickTextContainer.SetActive(false);
            }

            if (chest.relicGrantPopup != null && chest.relicGrantPopup.gameObject.activeInHierarchy)
            {
                chest.relicGrantPopup.gameObject.SetActive(false);
            }

            if (chest.navCurtain != null && chest.navCurtain.enabled)
            {
                chest.navCurtain.enabled = false;
            }

            if (chest.playfieldMouseDetection != null && !chest.playfieldMouseDetection.activeSelf)
            {
                chest.playfieldMouseDetection.SetActive(true);
            }

            if (Camera.main != null)
            {
                var pos = Camera.main.transform.position;
                Camera.main.transform.position = new UnityEngine.Vector3(pos.x, chest.cameraNavY, pos.z);
            }

            global::PauseMenu.PauseBlock = false;

            CoopRewardState.TreasurePhaseActive = false;
            CoopRewardState.TreasureAwaitingHostNavigation = false;
            CoopRewardState.WaitingForOtherPlayers = false;

            log?.LogInfo("[CoopNavigate] Client dismissed ChestScenarioController UI for navigation");
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] DismissChestForNavigation failed: {ex.Message}");
        }
    }

    private static void DismissDialogueForNavigation(
        global::RNG.Scenarios.DialogueSystemScenario dialogue,
        BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            if (dialogue.mainTextAnimatorCanvas != null
                && dialogue.mainTextAnimatorCanvas.gameObject.activeInHierarchy)
            {
                dialogue.mainTextAnimatorCanvas.gameObject.SetActive(false);
            }

            if (dialogue.navCurtain != null && dialogue.navCurtain.enabled)
            {
                dialogue.navCurtain.enabled = false;
            }

            if (dialogue.fullCurtain != null && dialogue.fullCurtain.enabled)
            {
                dialogue.fullCurtain.enabled = false;
            }

            global::PauseMenu.PauseBlock = false;

            CoopRewardState.TextScenarioPhaseActive = false;
            CoopRewardState.TextScenarioAwaitingHostNavigation = false;
            CoopRewardState.WaitingForOtherPlayers = false;

            log?.LogInfo("[CoopNavigate] Client dismissed DialogueSystemScenario UI for navigation");
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] DismissDialogueForNavigation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Fade out the nav curtain if any scenario controller has one wired up.
    /// Clients never ran Skip/StartNavigation, so the curtain may still be
    /// covering the playfield. Best-effort — fall through if not found.
    /// </summary>
    private static void TryFadeOutNavCurtain(BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            var chests = Resources.FindObjectsOfTypeAll<global::Scenarios.ChestScenarioController>();
            if (chests != null)
            {
                foreach (var c in chests)
                {
                    if (c == null || c.navCurtain == null)
                    {
                        continue;
                    }

                    if (c.gameObject.activeInHierarchy)
                    {
                        c.navCurtain.enabled = false;
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] TryFadeOutNavCurtain failed: {ex.Message}");
        }
    }

    private static void InvokePostBattleStartNavigation(BepInEx.Logging.ManualLogSource log, int childNodeCount)
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
            $"[CoopNavigate] Found PostBattleController '{target.name}' (active={target.gameObject.activeInHierarchy}) — running client nav setup (children={childNodeCount})");

        if (!target.gameObject.activeInHierarchy)
        {
            target.gameObject.SetActive(true);
        }

        // CRITICAL: do NOT call SkipMimicNavigation / StartNavigation on the client.
        // Both go through PostBattleController.StartNavigation, which has this guard:
        //   if (StaticGameData.currentNode == null || ChildNodes.Length == 0)
        //   { _winTimeline.SetActive(true); return; }
        // On the client, currentNode is ALWAYS null (the map is heartbeat-synced;
        // the live MapNode tree is host-only). The guard would activate the
        // floor-victory _winTimeline (which fades the screen to black for the
        // cinematic), skip slot-manager setup entirely, and leave us aiming a
        // nav ball at a black screen. That was the symptom: clients in AIMING
        // state, nav ball alive, but the win timeline overlay covered everything.
        //
        // Instead, replicate just the safe pieces of StartNavigation manually:
        //   1. Force-deactivate _winTimeline (in case it was already turned on).
        //   2. Run the public peg-prep methods that StartNavigation would call.
        //   3. Activate the three SlotManagers using childNodeCount from the
        //      network event (host's authoritative count) and a neutral color
        //      (clients don't have the real ChildNodes / RoomType).
        //   4. Arm the nav ball.

        ForceDeactivateWinTimeline(target, log);

        var bc = HarmonyLib.AccessTools.Field(typeof(global::Battle.PostBattleController), "_battleController")
            ?.GetValue(target) as global::Battle.BattleController;
        if (bc == null)
        {
            log?.LogWarning("[CoopNavigate] Client: PostBattleController._battleController is null");
            return;
        }

        try
        {
            bc.RemoveClearedPegs();
            bc.ResetDamageTally();
            bc.PreparePegsForNavigation();
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] Client peg-prep failed: {ex.Message}");
        }

        ActivateSlotManagers(target, childNodeCount, log);

        try
        {
            bc.ArmNavigationBall();
            log?.LogInfo("[CoopNavigate] Client armed nav ball");
        }
        catch (System.Exception ex)
        {
            log?.LogError($"[CoopNavigate] Client ArmNavigationBall threw: {ex.Message}");
        }
    }

    /// <summary>
    /// Force-deactivate PostBattleController._winTimeline. The native
    /// StartNavigation(currentNode==null) path may have already turned it on
    /// before we got here, and even if it didn't, defensively flipping it off
    /// prevents any stale activation from leaving a black-screen overlay.
    /// </summary>
    private static void ForceDeactivateWinTimeline(global::Battle.PostBattleController pbc, BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            var f = HarmonyLib.AccessTools.Field(typeof(global::Battle.PostBattleController), "_winTimeline");
            var go = f?.GetValue(pbc) as GameObject;
            if (go != null && go.activeInHierarchy)
            {
                go.SetActive(false);
                log?.LogInfo("[CoopNavigate] Force-deactivated PostBattleController._winTimeline");
            }
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] ForceDeactivateWinTimeline failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Activate left/center/right SlotManagers based on the network-event child
    /// count. Mirrors PostBattleController.StartNavigation's slot setup but
    /// without depending on StaticGameData.currentNode (null on clients) — uses
    /// a neutral white tint and the first available MapNode icon as a stand-in.
    /// </summary>
    private static void ActivateSlotManagers(
        global::Battle.PostBattleController pbc,
        int childCount,
        BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            var leftField = HarmonyLib.AccessTools.Field(typeof(global::Battle.PostBattleController), "_leftSlotManager");
            var centerField = HarmonyLib.AccessTools.Field(typeof(global::Battle.PostBattleController), "_centerSlotManager");
            var rightField = HarmonyLib.AccessTools.Field(typeof(global::Battle.PostBattleController), "_rightSlotManager");

            var left = leftField?.GetValue(pbc) as global::Battle.SlotManager;
            var center = centerField?.GetValue(pbc) as global::Battle.SlotManager;
            var right = rightField?.GetValue(pbc) as global::Battle.SlotManager;

            if (left == null || center == null || right == null)
            {
                log?.LogWarning("[CoopNavigate] Client: SlotManagers not all wired on PBC");
                return;
            }

            var color = Color.white;
            Sprite icon = null;
            try
            {
                var anyNode = Resources.FindObjectsOfTypeAll<global::Worldmap.MapNode>();
                if (anyNode != null && anyNode.Length > 0)
                {
                    icon = anyNode[0].activeIcon;
                }
            }
            catch
            {
                // Best-effort icon lookup — fine if it fails.
            }

            left.gameObject.SetActive(true);
            right.gameObject.SetActive(true);
            center.gameObject.SetActive(true);

            // childCount of 1 → all three slots point to the same room (HalfNav
            // on left/right, ConfigureForNavigation on center). childCount > 1 →
            // left/right are independent; center is dud unless > 2.
            if (childCount <= 1)
            {
                left.ConfigureHalfNavigation(color);
                right.ConfigureHalfNavigation(color);
                center.ConfigureForNavigation(icon, color);
            }
            else
            {
                left.ConfigureForNavigation(icon, color);
                right.ConfigureForNavigation(icon, color);
                if (childCount > 2)
                {
                    center.ConfigureForNavigation(icon, color);
                }
                else
                {
                    center.ConfigureDudNavigation();
                }
            }

            log?.LogInfo($"[CoopNavigate] Client activated {childCount} slot manager(s)");
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[CoopNavigate] ActivateSlotManagers failed: {ex.Message}");
        }
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
