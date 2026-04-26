using System;
using HarmonyLib;
using Map;
using UnityEngine;
using Worldmap;
using static Multipeglin.Patches.MultiplayerClientPatches;
using Random = UnityEngine.Random;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class MapControllerPatches
{
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
        if (!ShouldSuppressClientLogic)
        {
            return;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] CreateMapDataLists ran on client (lists unused, prevents NRE)");
    }

    /// <summary>Block post-processing of map on client (relic-based node changes).</summary>
    [HarmonyPatch(typeof(Map.MapController), "PostProcessMap")]
    [HarmonyPrefix]
    public static bool MapController_PostProcessMap_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }

    /// <summary>Block seeding map contents on client.</summary>
    [HarmonyPatch(typeof(Map.MapController), "SeedMapContents")]
    [HarmonyPrefix]
    public static bool MapController_SeedMapContents_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }

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
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked LoadSceneFromMapData — host will send transitions");

        // Clear fade curtain — the game started a fade-to-black before we blocked the load
        try
        {
            var curtain = UnityEngine.Object.FindObjectOfType<PeglinUI.FadeCurtain>();
            curtain?.FadeOut();
        }
        catch
        {
        }

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
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked MapController.ResolveNode (client — scene handled by NodeActivatedClientHandler)");
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
            catch
            {
            }
            // Swallow Start exceptions on host so Unity doesn't mark the MC broken.
            return null;
        }

        if (!ShouldSuppressClientLogic)
        {
            return __exception;
        }

        // CLIENT: re-apply host node types (Start set them to NONE via blocked GenerateRoomType)
        MapControllerStartCompleted = true;

        if (__exception != null)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] MapController.Start threw on client (swallowed): {__exception.Message}");
        }

        try
        {
            if (MultiplayerPlugin.Services?.TryResolve<GameState.GameStateApplyService>(out var applySvc) == true)
            {
                applySvc.ReapplyLastMapState();
            }
        }
        catch
        {
        }

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
        if (!IsHosting)
        {
            return true;
        }

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
                    {
                        player.transform.position = prevNode.transform.position;
                    }
                }
                catch
                {
                }

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
        if (IsHosting)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Intro: IntroFade entered");
        }
    }

    [HarmonyPatch(typeof(Map.MapController), "IntroCameraPan")]
    [HarmonyPostfix]
    public static void MapController_IntroCameraPan_Postfix()
    {
        if (IsHosting)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Intro: IntroCameraPan entered");
        }
    }

    [HarmonyPatch(typeof(Map.MapController), "PostFadeInit")]
    [HarmonyPostfix]
    public static void MapController_PostFadeInit_Postfix()
    {
        if (IsHosting)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Intro: PostFadeInit entered");
        }
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
        // Fix: only swap when the stale MC belongs to a DIFFERENT act (cross-act).
        // Compare `Act` (prefab-baked int) — NOT `gameObject.scene.name`, because once
        // the MC has been DDoL'd its scene.name is "DontDestroyOnLoad" regardless of
        // which map it came from, so scene-name comparison always mis-fires on
        // same-act re-entries and destroys the legitimate DDoL'd singleton. The
        // consequence of that mistake: each ForestMap->Battle->ForestMap re-entry
        // handed control to a brand-new MC with _firstLoad=true, which captured
        // `_playerInitialPosition` at the scene's default spawn and made PrePanWait
        // scroll the camera in from far away.
        if (!IsHosting && MapController.instance != null && MapController.instance != __instance)
        {
            try
            {
                var stale = MapController.instance;
                var sameAct = stale.Act == __instance.Act
                    && string.Equals(stale.mapNameLocKey, __instance.mapNameLocKey, StringComparison.Ordinal);
                if (!sameAct)
                {
                    var staleScene = stale.gameObject.scene.name;
                    var newScene = __instance.gameObject.scene.name;
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Client: destroying stale MapController (act {stale.Act} '{stale.mapNameLocKey}' from '{staleScene}') so new MC (act {__instance.Act} '{__instance.mapNameLocKey}' from '{newScene}') can take over");
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
}
