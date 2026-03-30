using System;
using Battle;
using Data;
using HarmonyLib;
using Loading;
using Map;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.Events.Network.Map;
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
    // BLOCK CLIENT GAME LOGIC — client is a dumb renderer
    // =========================================================================

    [HarmonyPatch(typeof(BattleController), "Update")]
    [HarmonyPrefix]
    public static bool BattleController_Update_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(SaveManager), "SaveRun")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveRun_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(SaveManager), "SaveBase")]
    [HarmonyPrefix]
    public static bool SaveManager_SaveBase_Prefix() => !ShouldSuppressClientLogic;

    [HarmonyPatch(typeof(GameInit), "Start")]
    [HarmonyPrefix]
    public static bool GameInit_Start_Prefix() => !ShouldSuppressClientLogic;

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
    [HarmonyPatch(typeof(Map.MapController), "CreateMapDataLists")]
    [HarmonyPrefix]
    public static bool MapController_CreateMapDataLists_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked CreateMapDataLists — host will send node types");
        return false;
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
    /// </summary>
    [HarmonyPatch(typeof(Map.MapController), "LoadSceneFromMapData")]
    [HarmonyPrefix]
    public static bool MapController_LoadSceneFromMapData_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked LoadSceneFromMapData — host will send transitions");
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
    /// Block deck shuffle on client. BattleController.Start() calls ShuffleCompleteDeck()
    /// which re-shuffles with the client's own RNG, producing wrong orb order.
    /// Our DeckApplier syncs the correct shuffledDeck order from the host.
    /// </summary>
    [HarmonyPatch(typeof(DeckManager), "ShuffleCompleteDeck")]
    [HarmonyPrefix]
    public static bool DeckManager_ShuffleCompleteDeck_Prefix()
    {
        if (!ShouldSuppressClientLogic) return true;
        MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Blocked ShuffleCompleteDeck — host will send deck order");
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
        });
        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Host activated node at ({pos.x:F1}, {pos.y:F1}), battle={battleName}");
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
}
