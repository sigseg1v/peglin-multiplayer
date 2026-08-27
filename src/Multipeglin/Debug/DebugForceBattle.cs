using System;
using System.Collections.Generic;
using Data;
using HarmonyLib;
using UnityEngine;
using Worldmap;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Debug;

/// <summary>
/// Debug environment variable for pinning every battle node to a specific battle.
///
/// MULTIPEGLIN_FORCE_BATTLE (e.g. "PlantEncounter3")
///   Host-only: after MapController picks a battle for a node, swap in the named
///   MapDataBattle instead (substring, case-insensitive). Every BATTLE / MINI_BOSS
///   node on the map then loads that encounter, so a specific pegboard layout can be
///   reproduced from the first floor without hunting the map RNG for it.
///
///   Unlike MULTIPEGLIN_FORCE_NODE this needs no map path: battle MapData is only
///   materialised when a node is entered, so it cannot be searched for ahead of time.
///
///   The MapDataType (easy/random/elite) the game chose is preserved so save/serialise
///   paths stay consistent; only the asset changes.
/// </summary>
[HarmonyPatch]
public static class DebugForceBattle
{
    private const string EnvVar = "MULTIPEGLIN_FORCE_BATTLE";

    private static bool _parsed;
    private static string _hint;
    private static MapDataBattle _resolved;
    private static bool _resolveFailed;

    private static void Parse()
    {
        if (_parsed)
        {
            return;
        }

        _parsed = true;
        var raw = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        _hint = raw.Trim();
        MultiplayerPlugin.Logger?.LogInfo($"[DebugForceBattle] Will force every battle node to '{_hint}'");
    }

    [HarmonyPatch(typeof(Map.MapController), "GenerateRandomMapData")]
    [HarmonyPostfix]
    public static void MapController_GenerateRandomMapData_Postfix(Map.MapController __instance, MapNode node)
    {
        Parse();
        if (_hint == null || node == null || !IsHosting)
        {
            return;
        }

        if (node.RoomType != RoomType.BATTLE && node.RoomType != RoomType.MINI_BOSS)
        {
            return;
        }

        var forced = Resolve(__instance);
        if (forced == null || ReferenceEquals(forced, node.MapData))
        {
            return;
        }

        // Keep whatever background the game just assigned — the forced asset is a
        // shared ScriptableObject and its background field is runtime state.
        if (node.MapData != null)
        {
            forced.background = node.MapData.background;
            forced.pegboardFrame = node.MapData.pegboardFrame;
        }

        var previous = node.MapData?.name ?? "<null>";
        node.MapData = forced;
        MultiplayerPlugin.Logger?.LogInfo(
            $"[DebugForceBattle] {node.gameObject.name}: {previous} → {forced.name} (type={node.MapDataType})");
    }

    private static MapDataBattle Resolve(Map.MapController mc)
    {
        if (_resolved != null || _resolveFailed)
        {
            return _resolved;
        }

        foreach (var candidate in Candidates(mc))
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.name))
            {
                continue;
            }

            // Match the battle asset name or its pegboard name — the interesting
            // axis for sync testing is the peg layout (moving/sliding/long pegs),
            // and layout names are what the addressables catalog lists.
            var layoutName = candidate.pegLayout?.name;
            if (candidate.name.IndexOf(_hint, StringComparison.OrdinalIgnoreCase) >= 0
                || (!string.IsNullOrEmpty(layoutName)
                    && layoutName.IndexOf(_hint, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                _resolved = candidate;
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[DebugForceBattle] Resolved '{_hint}' → {candidate.name} (pegLayout={candidate.pegLayout?.name ?? "<none>"})");
                return _resolved;
            }
        }

        _resolveFailed = true;
        MultiplayerPlugin.Logger?.LogWarning(
            $"[DebugForceBattle] No MapDataBattle matched '{_hint}' — battles left untouched");
        return null;
    }

    private static IEnumerable<MapDataBattle> Candidates(Map.MapController mc)
    {
        // Prefer the act's own pools so a forced battle stays act-appropriate.
        foreach (var field in new[] { "_allEasyBattles", "_allRandomBattles", "_potentialEliteBattles" })
        {
            if (AccessTools.Field(typeof(Map.MapController), field)?.GetValue(mc) is List<MapDataBattle> pool)
            {
                foreach (var battle in pool)
                {
                    yield return battle;
                }
            }
        }

        // Fall back to every loaded asset (covers other acts and mimic/boss data).
        foreach (var battle in Resources.FindObjectsOfTypeAll<MapDataBattle>())
        {
            yield return battle;
        }
    }
}
