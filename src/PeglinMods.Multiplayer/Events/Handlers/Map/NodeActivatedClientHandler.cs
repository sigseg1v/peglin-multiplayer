using System;
using System.Linq;
using Data;
using HarmonyLib;
using Loading;
using Map;
using PeglinMods.Multiplayer.Events.Network.Map;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Patches;
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

            if (string.IsNullOrEmpty(e.BattleName))
            {
                log?.LogWarning("[NodeActivated] No battle name received, cannot load correct battle");
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

            // Load Battle scene — set flag so our PeglinSceneLoader patch allows it
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
