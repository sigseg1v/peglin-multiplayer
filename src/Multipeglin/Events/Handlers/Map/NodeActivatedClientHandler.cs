using System;
using System.Linq;
using Data;
using Data.Scenarios;
using HarmonyLib;
using Loading;
using Map;
using Multipeglin.Events.Network.Map;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using Multipeglin.Patches;
using Scenarios;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Map;

public sealed class NodeActivatedClientHandler : IClientHandler<NodeActivatedEvent>
{
    public void Handle(NodeActivatedEvent e)
    {
        var log = MultiplayerPlugin.Logger;
        try
        {
            var mode = MultiplayerPlugin.Services?.Resolve<IMultiplayerMode>();
            if (mode == null || !mode.IsSpectating) return;

            log?.LogInfo($"[NodeActivated] Host battle={e.BattleName} at ({e.PosX:F1},{e.PosY:F1})");

            // Store host's RNG state for pegboard generation sync
            if (!string.IsNullOrEmpty(e.RngState))
            {
                MultiplayerClientPatches.PendingBattleRngState = e.RngState;
                log?.LogInfo("[NodeActivated] Stored host RNG state for pegboard sync");
            }

            // PegMinigame node — load the scene so the client can play independently
            if (string.IsNullOrEmpty(e.BattleName) && !string.IsNullOrEmpty(e.MapDataName))
            {
                var minigameData = FindPegMinigameData(e.MapDataName, log);
                if (minigameData != null)
                {
                    log?.LogInfo($"[NodeActivated] PegMinigame node — loading scene for spectating (asset={e.MapDataName})");
                    // PegMinigameManager.Initialize instantiates _mapData.background and
                    // _mapData.pegboardFrame. On the host those are populated by
                    // MapController.LoadSceneFromMapData, which we skip on the client.
                    // Without this assignment, Initialize NREs and the scene renders
                    // blank to the player.
                    PegMinigameVisualAssigner.Assign(minigameData, log, "NodeActivated");
                    StaticGameData.dataToLoad = minigameData;
                    GameState.Appliers.MapStateApplier.AwaitingHostSceneConfirmation = "PegMinigame";
                    MultiplayerClientPatches.AllowNextSceneLoad = true;
                    PeglinSceneLoader.Instance?.LoadScene(PeglinSceneLoader.Scene.PEG_MINIGAME);
                    return;
                }

                // TextScenario node — load the scene so the client can spectate dialogue
                var scenarioData = FindTextScenarioData(e.MapDataName, log);
                if (scenarioData != null)
                {
                    log?.LogInfo($"[NodeActivated] TextScenario node — loading scene for spectating (asset={e.MapDataName})");
                    StaticGameData.dataToLoad = scenarioData;
                    TextScenarioHoverTracker.Reset();
                    GameState.Appliers.MapStateApplier.AwaitingHostSceneConfirmation = "TextScenario";
                    MultiplayerClientPatches.AllowNextSceneLoad = true;
                    PeglinSceneLoader.Instance?.LoadScene(PeglinSceneLoader.Scene.TEXT_SCENARIO);
                    return;
                }

                // ShopScenario node — load the scene so the client can shop interactively
                var shopData = FindShopData(e.MapDataName, log);
                if (shopData != null)
                {
                    log?.LogInfo($"[NodeActivated] ShopScenario node — loading scene for interactive shopping (asset={e.MapDataName})");
                    StaticGameData.dataToLoad = shopData;
                    GameState.Appliers.MapStateApplier.AwaitingHostSceneConfirmation = "ShopScenario";
                    MultiplayerClientPatches.AllowNextSceneLoad = true;
                    PeglinSceneLoader.Instance?.LoadScene(PeglinSceneLoader.Scene.SHOP_SCENARIO);
                    return;
                }

                // Treasure node — load the scene so the client gets native relic UI
                var treasureData = FindTreasureData(e.MapDataName, log);
                if (treasureData != null)
                {
                    log?.LogInfo($"[NodeActivated] Treasure node — loading scene for native relic selection (asset={e.MapDataName})");
                    StaticGameData.dataToLoad = treasureData;
                    GameState.Appliers.MapStateApplier.AwaitingHostSceneConfirmation = "Treasure";
                    MultiplayerClientPatches.AllowNextSceneLoad = true;
                    PeglinSceneLoader.Instance?.LoadScene(PeglinSceneLoader.Scene.TREASURE);
                    return;
                }

                // Fall through — MapStateApplier will handle if lookup failed
            }

            // For non-battle nodes (treasure, shop),
            // BattleName is empty. Let the MapStateApplier handle scene transition
            // via the next SyncAll which carries the correct ActiveScene.
            if (string.IsNullOrEmpty(e.BattleName))
            {
                log?.LogInfo("[NodeActivated] Non-battle node — MapStateApplier will handle scene transition");
                return;
            }

            // Find the MapDataBattle asset by name — it's a ScriptableObject loaded in memory
            var allBattles = Resources.FindObjectsOfTypeAll<MapDataBattle>();
            var match = allBattles.FirstOrDefault(b => b.name == e.BattleName);

            if (match == null)
            {
                // Cross-scene cache populated while on map scenes — Castle/Mines/Core
                // battles may not be in Resources once their map scene unloads.
                match = GameState.Appliers.MapStateApplier.TryGetCachedBattle(e.BattleName);
            }

            if (match == null)
            {
                log?.LogWarning($"[NodeActivated] MapDataBattle '{e.BattleName}' not found in {allBattles.Length} loaded assets");
                return;
            }

            // Assign background + pegboardFrame from MapController before loading battle.
            // Normally MapController.ResolveNode() does this, but we block LoadSceneFromMapData.
            // Background comes from MapController.backgroundData[backgroundIndex].Background
            // PegboardFrame comes from MapController.battlePegboardFrame[backgroundIndex]
            AssignBattleVisuals(match, log);

            log?.LogInfo($"[NodeActivated] Found MapDataBattle '{match.name}', pegLayout={match.pegLayout?.name}, " +
                $"starterSpawns={match.starterSpawns?.Count ?? -1}, waves={match.waveGroups?.Length ?? -1}, " +
                $"background={match.background?.name ?? "NULL"}, pegboardFrame={match.pegboardFrame?.name ?? "NULL"}");

            // Set the battle data directly — this is what BattleController.Awake reads
            StaticGameData.dataToLoad = match;

            // Load Battle scene — set flags so our patches allow it and ignore stale map syncs
            GameState.Appliers.MapStateApplier.AwaitingHostSceneConfirmation = "Battle";
            var sceneLoader = PeglinSceneLoader.Instance;
            if (sceneLoader != null)
            {
                log?.LogInfo("[NodeActivated] Loading Battle scene with correct battle data");
                MultiplayerClientPatches.AllowNextSceneLoad = true;
                sceneLoader.LoadScene(PeglinSceneLoader.Scene.BATTLE);
            }
            else
            {
                log?.LogWarning("[NodeActivated] PeglinSceneLoader.Instance is null, using SceneManager fallback");
                UnityEngine.SceneManagement.SceneManager.LoadScene("Battle");
            }
        }
        catch (Exception ex)
        {
            log?.LogError($"[NodeActivated] Failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static MapDataPegMinigame FindPegMinigameData(string assetName, BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll<MapDataPegMinigame>();
            foreach (var asset in all)
            {
                if (asset.name == assetName)
                    return asset;
            }
            // Don't warn here — may be a TextScenario asset, not a PegMinigame
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[NodeActivated] FindPegMinigameData failed: {ex.Message}");
        }
        return null;
    }

    private static MapDataScenario FindTextScenarioData(string assetName, BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll<MapDataScenario>();
            foreach (var asset in all)
            {
                if (asset.name == assetName)
                    return asset;
            }
            log?.LogWarning($"[NodeActivated] MapDataScenario '{assetName}' not found in {all.Length} loaded assets");
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[NodeActivated] FindTextScenarioData failed: {ex.Message}");
        }
        return null;
    }

    private static MapDataShop FindShopData(string assetName, BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll<MapDataShop>();
            foreach (var asset in all)
            {
                if (asset.name == assetName)
                    return asset;
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[NodeActivated] FindShopData failed: {ex.Message}");
        }
        return null;
    }

    private static MapDataTreasure FindTreasureData(string assetName, BepInEx.Logging.ManualLogSource log)
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll<MapDataTreasure>();
            foreach (var asset in all)
            {
                if (asset.name == assetName)
                    return asset;
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[NodeActivated] FindTreasureData failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Assign background and pegboardFrame from MapController's scene-specific data.
    /// The game normally does this in MapController.ResolveNode() before loading battle.
    /// </summary>
    private static void AssignBattleVisuals(MapDataBattle battle, BepInEx.Logging.ManualLogSource log)
        => BattleVisualAssigner.Assign(battle, log, "NodeActivated");
}

/// <summary>
/// Assigns background and pegboardFrame on a MapDataPegMinigame. The game's
/// MapController.LoadSceneFromMapData populates these from pegMinigamePegboardFrame,
/// but we skip that call on the client, so PegMinigameManager.Initialize would
/// NRE when it instantiates the background / pegboardFrame.
/// </summary>
internal static class PegMinigameVisualAssigner
{
    public static void Assign(MapDataPegMinigame data, BepInEx.Logging.ManualLogSource log, string tag)
    {
        try
        {
            var mc = MapController.instance;
            if (mc == null)
            {
                log?.LogWarning($"[{tag}] MapController.instance is null — cannot assign PegMinigame visuals");
                return;
            }

            var bgDataField = AccessTools.Field(typeof(MapController), "backgroundData");
            var bgIndexField = AccessTools.Field(typeof(MapController), "backgroundIndex");
            var bgData = bgDataField?.GetValue(mc) as Array;
            var bgIndex = bgIndexField != null ? (int)bgIndexField.GetValue(mc) : 0;

            if (data.background == null && bgData != null && bgIndex >= 0 && bgIndex < bgData.Length)
            {
                var bgEntry = bgData.GetValue(bgIndex);
                if (bgEntry != null)
                {
                    var bgGo = bgEntry.GetType().GetField("Background")?.GetValue(bgEntry) as GameObject
                        ?? bgEntry.GetType().GetProperty("Background")?.GetValue(bgEntry) as GameObject;

                    if (bgGo != null)
                    {
                        data.background = bgGo;
                        log?.LogInfo($"[{tag}] Assigned PegMinigame background: {bgGo.name}");
                    }
                }
            }

            if (data.pegboardFrame == null)
            {
                var frameField = AccessTools.Field(typeof(MapController), "pegMinigamePegboardFrame");
                var frames = frameField?.GetValue(mc) as GameObject[];
                if (frames != null && bgIndex >= 0 && bgIndex < frames.Length)
                {
                    data.pegboardFrame = frames[bgIndex];
                    log?.LogInfo($"[{tag}] Assigned PegMinigame pegboardFrame: {frames[bgIndex]?.name ?? "NULL"}");
                }
                else if (frames != null && frames.Length > 0)
                {
                    data.pegboardFrame = frames[0];
                    log?.LogInfo($"[{tag}] Assigned PegMinigame pegboardFrame (fallback idx 0): {frames[0]?.name ?? "NULL"}");
                }
            }

            // Also ensure bouncerPrefab is set (MapController.GetPegMinigameScenario
            // copies this from backgroundData). PegMinigameManager.CreateBouncers
            // instantiates from it.
            if (data.bouncerPrefab == null && bgData != null && bgIndex >= 0 && bgIndex < bgData.Length)
            {
                var bgEntry = bgData.GetValue(bgIndex);
                if (bgEntry != null)
                {
                    var bouncer = bgEntry.GetType().GetField("BouncerPrefab")?.GetValue(bgEntry) as GameObject
                        ?? bgEntry.GetType().GetProperty("BouncerPrefab")?.GetValue(bgEntry) as GameObject;

                    if (bouncer != null)
                    {
                        data.bouncerPrefab = bouncer;
                        log?.LogInfo($"[{tag}] Assigned PegMinigame bouncerPrefab: {bouncer.name}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[{tag}] PegMinigameVisualAssigner.Assign failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Shared helper to assign background and pegboardFrame on a MapDataBattle from
/// MapController's scene-specific arrays. Used by both NodeActivatedClientHandler
/// (normal battle nodes) and MapStateApplier (scenario battles, where the node
/// activation path skips battle visual assignment because the host went into
/// TextScenario first).
/// </summary>
internal static class BattleVisualAssigner
{
    public static void Assign(MapDataBattle battle, BepInEx.Logging.ManualLogSource log, string tag)
    {
        try
        {
            var mc = MapController.instance;
            if (mc == null)
            {
                log?.LogWarning($"[{tag}] MapController.instance is null — cannot assign battle visuals");
                return;
            }

            var bgDataField = AccessTools.Field(typeof(MapController), "backgroundData");
            var bgIndexField = AccessTools.Field(typeof(MapController), "backgroundIndex");
            var bgData = bgDataField?.GetValue(mc) as Array;
            var bgIndex = bgIndexField != null ? (int)bgIndexField.GetValue(mc) : 0;

            if (battle.background == null && bgData != null && bgIndex >= 0 && bgIndex < bgData.Length)
            {
                var bgEntry = bgData.GetValue(bgIndex);
                if (bgEntry != null)
                {
                    var bgGo = bgEntry.GetType().GetField("Background")?.GetValue(bgEntry) as GameObject
                        ?? bgEntry.GetType().GetProperty("Background")?.GetValue(bgEntry) as GameObject;

                    if (bgGo != null)
                    {
                        battle.background = bgGo;
                        log?.LogInfo($"[{tag}] Assigned background: {bgGo.name}");
                    }
                }
            }

            if (battle.pegboardFrame == null)
            {
                var frameField = AccessTools.Field(typeof(MapController), "battlePegboardFrame");
                var frames = frameField?.GetValue(mc) as GameObject[];
                if (frames != null && bgIndex >= 0 && bgIndex < frames.Length)
                {
                    battle.pegboardFrame = frames[bgIndex];
                    log?.LogInfo($"[{tag}] Assigned pegboardFrame: {frames[bgIndex]?.name ?? "NULL"}");
                }
                else if (frames != null && frames.Length > 0)
                {
                    battle.pegboardFrame = frames[0];
                    log?.LogInfo($"[{tag}] Assigned pegboardFrame (fallback idx 0): {frames[0]?.name ?? "NULL"}");
                }
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[{tag}] AssignBattleVisuals failed: {ex.Message}");
        }
    }
}
