using System;
using System.IO;
using System.Linq;
using Cruciball;
using HarmonyLib;
using Loading;
using Multipeglin.GameState;
using ToolBox.Serialization;
using UnityEngine;

namespace Multipeglin.Continue;

/// <summary>
/// Host-only loader that takes a parsed <see cref="ContinueSaveData"/> and
/// drives the game into the saved scene with restored coop state.
///
/// Two-phase:
///   1. <see cref="LaunchScene"/> writes the embedded RUN save bytes to disk,
///      configures LoadMapData, and triggers PostMainMenu → saved map scene.
///   2. <see cref="ApplyPendingCoopState"/> is invoked from a MapController
///      patch once the saved map scene is up; it pushes the saved
///      PlayerStates back into <see cref="CoopStateManager"/>.
/// </summary>
public static class ContinueLoader
{
    /// <summary>True between <see cref="LaunchScene"/> and the first apply.</summary>
    public static bool ApplyPending { get; private set; }

    /// <summary>
    /// Configure LoadMapData + write RUN bytes to disk, then trigger the scene
    /// load. The supplied save must already have been registered with
    /// <see cref="ContinueSession.Begin"/>.
    /// </summary>
    public static bool LaunchScene(ContinueSaveData data)
    {
        if (data == null)
        {
            MultiplayerPlugin.Logger?.LogWarning("[ContinueLoader] LaunchScene: null data");
            return false;
        }

        try
        {
            // Step 1: write RUN bytes to disk so the game's continue path finds them.
            if (!string.IsNullOrEmpty(data.GameRunSaveBase64))
            {
                var profile = DataSerializer.CurrentProfileIndex;
                var runPath = Path.Combine(Application.persistentDataPath, $"Save_{profile}r.data");
                var bytes = Convert.FromBase64String(data.GameRunSaveBase64);
                File.WriteAllBytes(runPath, bytes);
                MultiplayerPlugin.Logger?.LogInfo($"[ContinueLoader] wrote {bytes.Length} bytes to {runPath}");

                // Force the in-memory RUN dict to reload from the new file.
                try
                {
                    DataSerializer.LoadFile(DataSerializer.SaveType.RUN);
                }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ContinueLoader] LoadFile(RUN) threw: {ex.Message}");
                }
            }
            else
            {
                MultiplayerPlugin.Logger?.LogWarning("[ContinueLoader] save has no GameRunSaveBase64 — game continue may fail");
            }

            // Step 2: prep StaticGameData so MapController can resolve seed/cruciball
            try
            {
                if (!string.IsNullOrEmpty(data.Seed))
                {
                    StaticGameData.currentSeed = data.Seed;
                }
            }
            catch
            {
            }

            try
            {
                var cm = Resources.FindObjectsOfTypeAll<CruciballManager>()?.FirstOrDefault();
                if (cm != null && data.CruciballLevel >= 0)
                {
                    cm.currentCruciballLevel = data.CruciballLevel;
                }
            }
            catch
            {
            }

            // Step 3: configure LoadMapData (every PlayButton + RestartOnClick references
            // the same SO asset). Find any LoadMapData in memory.
            var loadMap = Resources.FindObjectsOfTypeAll<LoadMapData>().FirstOrDefault();
            if (loadMap == null)
            {
                MultiplayerPlugin.Logger?.LogWarning("[ContinueLoader] no LoadMapData found in memory; cannot drive continue");
                return false;
            }

            loadMap.SceneToLoad = (PeglinSceneLoader.Scene)data.MapScene;
            loadMap.NewGame = false;
            loadMap.ContinueGame = true;

            // Mirror PlayButton.ContinueClicked: ensure CurrentRunStats exists/reset
            try
            {
                if (StaticGameData.CurrentRunStats == null)
                {
                    StaticGameData.CurrentRunStats = new Stats.RunStats();
                }
                else
                {
                    StaticGameData.CurrentRunStats.Reset();
                }
            }
            catch
            {
            }

            ApplyPending = true;

            // Step 4: trigger the actual scene load. PlayButton.Load() goes via
            // POST_MAIN_MENU → routes to LoadMap.SceneToLoad. We can also load the
            // map scene directly; route via POST_MAIN_MENU to mirror the game's flow.
            if (PeglinSceneLoader.Instance != null)
            {
                PeglinSceneLoader.Instance.LoadScene(PeglinSceneLoader.Scene.POST_MAIN_MENU);
                MultiplayerPlugin.Logger?.LogInfo($"[ContinueLoader] launched POST_MAIN_MENU → {loadMap.SceneToLoad}");
            }
            else
            {
                MultiplayerPlugin.Logger?.LogWarning("[ContinueLoader] PeglinSceneLoader.Instance is null");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[ContinueLoader] LaunchScene failed: {ex}");
            ApplyPending = false;
            return false;
        }
    }

    /// <summary>
    /// Called from MapController.Start postfix when ApplyPending is set.
    /// Pushes the saved per-player coop states back into the CoopStateManager
    /// dictionary. The host's slot 0 state is also reflected in the game
    /// singletons because the game's continue path already restored them.
    /// </summary>
    public static void ApplyPendingCoopState()
    {
        if (!ApplyPending)
        {
            return;
        }

        try
        {
            var data = ContinueSession.ActiveSave;
            if (data == null)
            {
                MultiplayerPlugin.Logger?.LogWarning("[ContinueLoader] ApplyPending set but no ActiveSave");
                ApplyPending = false;
                return;
            }

            var services = MultiplayerPlugin.Services;
            if (services == null
                || services.TryResolve<CoopStateManager>(out var coopState) != true
                || coopState == null)
            {
                MultiplayerPlugin.Logger?.LogWarning("[ContinueLoader] CoopStateManager unavailable, deferring");
                return; // Leave ApplyPending true so we retry on the next hook fire
            }

            // Continue lobby strictly assigns each player to their saved slot
            // (PlayerRegistry rejects mismatches), so the saved SlotIndex is
            // the authoritative key — direct assignment lines up PlayerStates
            // with PlayerRegistry one-to-one.
            coopState.PlayerStates.Clear();
            foreach (var p in data.Players)
            {
                if (p?.State == null)
                {
                    continue;
                }

                p.State.SlotIndex = p.SlotIndex;
                if (string.IsNullOrEmpty(p.State.PlayerName))
                {
                    p.State.PlayerName = p.PlayerName;
                }

                coopState.PlayerStates[p.SlotIndex] = p.State;
            }

            // The game's continue path restored the ORIGINAL host's RUN bytes
            // into the singletons (deck, relics, hp, gold, status effects). If
            // a different player is now the host, those singletons hold the
            // wrong player's data — overwrite from the spliced PlayerStates[0].
            try
            {
                AccessTools.Property(typeof(CoopStateManager), nameof(CoopStateManager.ActivePlayerSlot))
                    ?.SetValue(coopState, 0);
            }
            catch
            {
            }

            try
            {
                if (coopState.PlayerStates.ContainsKey(0))
                {
                    coopState.LoadPlayerState(0);
                }
            }
            catch (Exception loadEx)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ContinueLoader] LoadPlayerState(0) threw: {loadEx.Message}");
            }

            MultiplayerPlugin.Logger?.LogInfo($"[ContinueLoader] restored {coopState.PlayerStates.Count} coop player states");

            // Rebuild turn order so the next round uses the saved roster.
            if (services.TryResolve<TurnManager>(out var tm) && tm != null)
            {
                try
                {
                    tm.Reset();
                    tm.BuildTurnOrder();
                }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ContinueLoader] turn rebuild failed: {ex.Message}");
                }
            }

            // FINAL STEP: re-anchor UnityEngine.Random.state to the cursor we
            // snapshotted right after map-load on save. Everything above this
            // (LoadPlayerState's deck shuffles, BuildTurnOrder, etc.) consumes
            // RNG that the original session never spent, so without this rewind
            // the next battle's pegboard / enemy spawn diverges from the
            // original run. Must run last so nothing else can consume RNG
            // before scene-driven game logic does.
            if (!string.IsNullOrEmpty(data.RandomStateJson))
            {
                try
                {
                    var st = JsonUtility.FromJson<UnityEngine.Random.State>(data.RandomStateJson);
                    UnityEngine.Random.state = st;
                    MultiplayerPlugin.Logger?.LogInfo("[ContinueLoader] restored UnityEngine.Random.state");
                }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ContinueLoader] restore Random.state failed: {ex.Message}");
                }
            }

            ApplyPending = false;
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[ContinueLoader] ApplyPendingCoopState failed: {ex}");
            ApplyPending = false;
        }
    }
}
