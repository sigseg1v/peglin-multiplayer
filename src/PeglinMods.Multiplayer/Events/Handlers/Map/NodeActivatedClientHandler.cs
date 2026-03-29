using System;
using System.Linq;
using Data;
using Loading;
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

            log?.LogInfo($"[NodeActivated] Found MapDataBattle '{match.name}', pegLayout={match.pegLayout?.name}, " +
                $"starterSpawns={match.starterSpawns?.Count ?? -1}, waves={match.waveGroups?.Length ?? -1}");

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
}
