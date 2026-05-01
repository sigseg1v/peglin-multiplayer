using System;
using System.IO;
using System.Linq;
using Cruciball;
using HarmonyLib;
using Map;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using ToolBox.Serialization;
using UnityEngine;

namespace Multipeglin.Continue;

/// <summary>
/// Host-only writer that snapshots co-op state + the game's RUN save into a
/// continue file. Triggered after each stage transition (post-battle map load
/// is the canonical hook).
/// </summary>
public static class ContinueSaver
{
    /// <summary>
    /// Capture and write a continue save. No-op on client. Catches all errors
    /// and logs — saving is best-effort and must never break gameplay.
    /// </summary>
    public static void Save(string reason)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                return;
            }

            if (services.TryResolve<IMultiplayerMode>(out var mode) != true || !mode.IsHosting)
            {
                return;
            }

            if (services.TryResolve<CoopStateManager>(out var coopState) != true || coopState == null)
            {
                MultiplayerPlugin.Logger?.LogInfo($"[ContinueSaver] {reason}: CoopStateManager unavailable, skipping save");
                return;
            }

            if (coopState.PlayerStates.Count == 0)
            {
                MultiplayerPlugin.Logger?.LogInfo($"[ContinueSaver] {reason}: no players in coop state, skipping save");
                return;
            }

            // Snapshot the host's current singleton state into slot 0 (or whoever is active)
            // before we read PlayerStates. Without this, the active player's most recent
            // changes (relic pickups, deck adds) live in singletons but not in PlayerStates.
            try
            {
                coopState.SaveActivePlayerState();
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ContinueSaver] SaveActivePlayerState threw: {ex.Message}");
            }

            // Read current map info
            var mc = MapController.instance;
            var act = mc != null ? mc.Act : 0;
            var floor = 0;
            var mapScene = 0;
            var mapNameLocKey = string.Empty;
            if (mc != null)
            {
                try
                {
                    floor = (int)(AccessTools.Field(typeof(MapController), "floorCount")?.GetValue(mc) ?? 0);
                }
                catch
                {
                }

                try
                {
                    mapScene = (int)mc.thisScene;
                }
                catch
                {
                }

                mapNameLocKey = mc.mapNameLocKey ?? string.Empty;
            }

            var cruciball = 0;
            try
            {
                var cm = Resources.FindObjectsOfTypeAll<CruciballManager>()?.FirstOrDefault();
                if (cm != null)
                {
                    cruciball = cm.currentCruciballLevel;
                }
            }
            catch { }

            var seed = StaticGameData.currentSeed ?? string.Empty;

            // Force the current MapController to flush its node state to disk
            // before we read the bytes. The game's OnSceneLoaded SaveRun is gated
            // on `!_firstLoad`, so on the very first node activation in a fresh
            // act (act-1 → act-2 transition) disk still holds the previous act's
            // node data. Reading disk at that moment captures a stale snapshot
            // where most current-map node names don't exist as keys, and on
            // continue load LoadNode silently early-exits via `!HasKey(name)` —
            // leaving 23 of 27 nodes at default RoomType.NONE with no icons,
            // no DrawLinesToChildren, no playable map.
            try
            {
                if (mc != null)
                {
                    var nodesField = AccessTools.Field(typeof(MapController), "_nodes");
                    if (nodesField?.GetValue(mc) is Worldmap.MapNode[] mapNodes)
                    {
                        foreach (var mn in mapNodes)
                        {
                            mn?.SaveNode();
                        }
                    }
                }

                ToolBox.Serialization.DataSerializer.SaveFile(ToolBox.Serialization.DataSerializer.SaveType.RUN);
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ContinueSaver] Pre-read SaveRun failed: {ex.Message}");
            }

            // Read the game's RUN save bytes (now freshly flushed above).
            var runSaveBase64 = (string)null;
            try
            {
                var profile = DataSerializer.CurrentProfileIndex;
                var runPath = Path.Combine(Application.persistentDataPath, $"Save_{profile}r.data");
                if (File.Exists(runPath))
                {
                    var bytes = File.ReadAllBytes(runPath);
                    runSaveBase64 = Convert.ToBase64String(bytes);
                }
                else
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ContinueSaver] {reason}: RUN save file not found at {runPath}");
                }
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ContinueSaver] Failed to read RUN save bytes: {ex.Message}");
            }

            var stageLabel = BuildStageLabel(mapNameLocKey, act, floor, cruciball);

            // Snapshot UnityEngine.Random.state at the moment of save so the
            // continue path can re-anchor to the same RNG cursor before the
            // next battle starts. See ContinueSaveData.RandomStateJson.
            var randomStateJson = (string)null;
            try
            {
                randomStateJson = JsonUtility.ToJson(UnityEngine.Random.state);
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ContinueSaver] capture Random.state failed: {ex.Message}");
            }

            var data = new ContinueSaveData
            {
                SchemaVersion = ContinueSaveData.SCHEMA_VERSION_CURRENT,
                ModVersion = MultiplayerPluginInfo.VERSION,
                GameVersion = Application.version,
                SavedAtUtc = DateTime.UtcNow.ToString("o"),
                Seed = seed,
                CruciballLevel = cruciball,
                Act = act,
                FloorCount = floor,
                MapScene = mapScene,
                StageLabel = stageLabel,
                GameRunSaveBase64 = runSaveBase64,
                RandomStateJson = randomStateJson,
            };

            foreach (var kvp in coopState.PlayerStates.OrderBy(p => p.Key))
            {
                data.Players.Add(new ContinuePlayer
                {
                    SlotIndex = kvp.Key,
                    PlayerName = kvp.Value.PlayerName ?? string.Empty,
                    ChosenClass = kvp.Value.ChosenClass,
                    State = kvp.Value,
                });
            }

            // Turn state — best-effort
            if (services.TryResolve<TurnManager>(out var tm) && tm != null)
            {
                data.TurnState.TurnOrder = tm.TurnOrder?.ToList() ?? new System.Collections.Generic.List<int>();
                data.TurnState.RoundNumber = tm.RoundNumber;
            }

            var playerNames = data.Players.Select(p => p.PlayerName);
            var path = ContinueFiles.BuildFilePath(playerNames, seed);

            ContinueFiles.Write(path, data);

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ContinueSaver] {reason}: wrote {Path.GetFileName(path)} " +
                $"(players={data.Players.Count}, stage='{stageLabel}', runBytes={runSaveBase64?.Length ?? 0} b64)");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[ContinueSaver] {reason} failed: {ex}");
        }
    }

    private static string BuildStageLabel(string mapNameLocKey, int act, int floor, int cruciball)
    {
        var actName = ResolveActName(mapNameLocKey, act);
        var floorPart = floor > 0 ? $"{actName}-{floor}" : actName;
        return cruciball > 0 ? $"{floorPart} Cruciball-{cruciball}" : floorPart;
    }

    private static string ResolveActName(string locKey, int act)
    {
        if (!string.IsNullOrEmpty(locKey))
        {
            try
            {
                var translated = I2.Loc.LocalizationManager.GetTranslation(locKey);
                if (!string.IsNullOrEmpty(translated))
                {
                    return translated;
                }
            }
            catch
            {
            }
        }

        return act > 0 ? $"Act-{act}" : "Act";
    }
}
