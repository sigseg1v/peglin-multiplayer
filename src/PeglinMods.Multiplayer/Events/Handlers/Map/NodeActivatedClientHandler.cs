using System;
using System.Linq;
using Data;
using Data.Scenarios;
using HarmonyLib;
using Loading;
using Map;
using PeglinMods.Multiplayer.Events.Network.Map;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Patches;
using Scenarios;
using UnityEngine;

namespace PeglinMods.Multiplayer.Events.Handlers.Map;

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

            // PegMinigame node — load the scene so the client can spectate
            if (string.IsNullOrEmpty(e.BattleName) && !string.IsNullOrEmpty(e.MapDataName))
            {
                var minigameData = FindPegMinigameData(e.MapDataName, log);
                if (minigameData != null)
                {
                    log?.LogInfo($"[NodeActivated] PegMinigame node — loading scene for spectating (asset={e.MapDataName})");
                    StaticGameData.dataToLoad = minigameData;
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
            GameState.Appliers.MapStateApplier.AwaitingHostBattleConfirmation = true;
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
    {
        try
        {
            var mc = MapController.instance;
            if (mc == null)
            {
                log?.LogWarning("[NodeActivated] MapController.instance is null — cannot assign background");
                return;
            }

            // Get backgroundData array and backgroundIndex via reflection
            var bgDataField = AccessTools.Field(typeof(MapController), "backgroundData");
            var bgIndexField = AccessTools.Field(typeof(MapController), "backgroundIndex");
            var bgData = bgDataField?.GetValue(mc) as Array;
            var bgIndex = bgIndexField != null ? (int)bgIndexField.GetValue(mc) : 0;

            if (bgData != null && bgIndex >= 0 && bgIndex < bgData.Length)
            {
                var bgEntry = bgData.GetValue(bgIndex);
                if (bgEntry != null)
                {
                    // BackgroundData has a Background field or property (GameObject)
                    var bgGo = bgEntry.GetType().GetField("Background")?.GetValue(bgEntry) as GameObject
                        ?? bgEntry.GetType().GetProperty("Background")?.GetValue(bgEntry) as GameObject;

                    if (bgGo != null)
                    {
                        battle.background = bgGo;
                        log?.LogInfo($"[NodeActivated] Assigned background: {bgGo.name}");
                    }
                }
            }

            // Get battlePegboardFrame array
            var frameField = AccessTools.Field(typeof(MapController), "battlePegboardFrame");
            var frames = frameField?.GetValue(mc) as GameObject[];
            if (frames != null && bgIndex >= 0 && bgIndex < frames.Length)
            {
                battle.pegboardFrame = frames[bgIndex];
                log?.LogInfo($"[NodeActivated] Assigned pegboardFrame: {frames[bgIndex]?.name ?? "NULL"}");
            }
            else if (frames != null && frames.Length > 0)
            {
                battle.pegboardFrame = frames[0];
                log?.LogInfo($"[NodeActivated] Assigned pegboardFrame (fallback idx 0): {frames[0]?.name ?? "NULL"}");
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[NodeActivated] AssignBattleVisuals failed: {ex.Message}");
        }
    }
}
