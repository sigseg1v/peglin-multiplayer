using System;
using System.Collections.Generic;
using System.Linq;
using Battle.StatusEffects;
using HarmonyLib;
using I2.Loc;
using Multipeglin.DI;
using Multipeglin.Events;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Coop;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using PeglinUI.RunSummary;
using Stats;
using UnityEngine;
using Worldmap;

namespace Multipeglin.Patches;

/// <summary>
/// When the host hits the RunSummary scene, capture the current RunStats +
/// per-player damage tallies and broadcast to every client so the client can
/// leave the "Host is viewing run summary..." waiting screen and render the
/// native summary.
/// </summary>
[HarmonyPatch(typeof(RunSummary), "OnEnable")]
public static class RunSummaryOnEnablePatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null) return;
            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting) return;

            var stats = StaticGameData.CurrentRunStats;
            if (stats == null)
            {
                MultiplayerPlugin.Logger?.LogWarning("[RunStatsSync] Host RunSummary opened but CurrentRunStats is null");
                return;
            }

            // Seed local per-player store so the native host-side summary gets
            // the per-player StatsLines injected alongside the client.
            var perPlayer = BuildPerPlayerStats(services);
            RunStatsSnapshotClientHandler.LatestPerPlayerStats = perPlayer;

            var snapshot = BuildSnapshot(stats, perPlayer);

            if (services.TryResolve<IGameEventRegistry>(out var registry))
            {
                registry.Dispatch(snapshot);
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[RunStatsSync] Host dispatched RunStatsSnapshot: won={snapshot.HasWon}, " +
                    $"dmg={snapshot.TotalDamageDealt}, players={snapshot.Players.Count}");
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[RunStatsSync] Host dispatch failed: {ex.Message}");
        }
    }

    private static List<PerPlayerStats> BuildPerPlayerStats(IServiceContainer services)
    {
        var result = new List<PerPlayerStats>();
        if (!services.TryResolve<CoopStateManager>(out var coop)) return result;

        foreach (var kvp in coop.PlayerStates.OrderBy(p => p.Key))
        {
            var state = kvp.Value;
            result.Add(new PerPlayerStats
            {
                SlotIndex = state.SlotIndex,
                PlayerName = string.IsNullOrEmpty(state.PlayerName) ? $"Player {state.SlotIndex + 1}" : state.PlayerName,
                DamageDealt = state.DamageDealt,
                DamageTaken = state.DamageTaken,
            });
        }
        return result;
    }

    private static RunStatsSnapshotEvent BuildSnapshot(RunStats stats, List<PerPlayerStats> players)
    {
        var ev = new RunStatsSnapshotEvent
        {
            HasWon = stats.hasWon,
            HasWonCore = stats.hasWonCore,
            IsCustomRun = stats.isCustomRun,
            VampireDealTaken = stats.vampireDealTaken,
            SelectedClass = (int)stats.selectedClass,
            CruciballLevel = stats.cruciballLevel,
            EndDateIso = stats.endDate.ToString("o"),
            Seed = stats.seed ?? "",
            RunTimerElapsedMs = stats.runTimerElapsedMilliseconds + (stats.runTimerSw?.ElapsedMilliseconds ?? 0),
            FinalHp = stats.finalHp,
            MaxHp = stats.maxHp,
            TotalDamageDealt = stats.totalDamageDealt,
            CoinsEarned = stats.coinsEarned,
            PegsHit = stats.pegsHit,
            PegsHitRefresh = stats.pegsHitRefresh,
            PegsHitCrit = stats.pegsHitCrit,
            BombsThrown = stats.bombsThrown,
            BombsThrownRigged = stats.bombsThrownRigged,
            Players = players,
        };

        if (stats.visitedRooms != null) ev.VisitedRooms = stats.visitedRooms.Select(r => (int)r).ToList();
        if (stats.visitedBosses != null) ev.VisitedBosses = stats.visitedBosses.Select(b => (int)b).ToList();
        if (stats.relics != null) ev.Relics = stats.relics.Select(r => (int)r).ToList();
        if (stats.challenges != null) ev.Challenges = stats.challenges.Select(c => (int)c).ToList();

        // Pack enum-indexed dicts into dense arrays so the wire format is stable.
        var statusMax = 0;
        foreach (var v in Enum.GetValues(typeof(StatusEffectType))) if ((int)v > statusMax) statusMax = (int)v;
        ev.StatusEffectStacks = Enumerable.Repeat(0, statusMax + 1).ToList();
        if (stats.stacksPerStatusEffect != null)
            foreach (var kv in stats.stacksPerStatusEffect)
                if ((int)kv.Key >= 0 && (int)kv.Key < ev.StatusEffectStacks.Count)
                    ev.StatusEffectStacks[(int)kv.Key] = kv.Value;

        var slimeMax = 0;
        foreach (var v in Enum.GetValues(typeof(global::Peg.SlimeType))) if ((int)v > slimeMax) slimeMax = (int)v;
        ev.SlimePegCounts = Enumerable.Repeat(0, slimeMax + 1).ToList();
        if (stats.slimePegsPerSlimeType != null)
            foreach (var kv in stats.slimePegsPerSlimeType)
                if ((int)kv.Key >= 0 && (int)kv.Key < ev.SlimePegCounts.Count)
                    ev.SlimePegCounts[(int)kv.Key] = kv.Value;

        if (stats.orbStats != null)
        {
            foreach (var kv in stats.orbStats)
            {
                var o = kv.Value;
                ev.Orbs.Add(new OrbStatsEntry
                {
                    Id = o.id,
                    Name = o.name,
                    DamageDealt = o.damageDealt,
                    TimesFired = o.timesFired,
                    TimesDiscarded = o.timesDiscarded,
                    TimesRemoved = o.timesRemoved,
                    Starting = o.starting,
                    AmountInDeck = o.amountInDeck,
                    LevelInstances = o.levelInstances != null ? (int[])o.levelInstances.Clone() : new int[3],
                });
            }
        }

        if (stats.enemyData != null)
        {
            foreach (var kv in stats.enemyData)
            {
                var en = kv.Value;
                ev.Enemies.Add(new EnemyStatsEntry
                {
                    Name = en.name,
                    AmountFought = en.amountFought,
                    MeleeDamageReceived = en.meleeDamageReceived,
                    RangedDamageReceived = en.rangedDamageReceived,
                    DefeatedBy = en.defeatedBy,
                });
            }
        }

        return ev;
    }
}

/// <summary>
/// Appends per-player "Damage done by X" / "Damage taken by X" StatsLines after
/// the native summary lines are built. Runs on both host and client — the stats
/// are seeded locally on the host in <see cref="RunSummaryOnEnablePatch"/> and
/// arrive via <see cref="RunStatsSnapshotClientHandler"/> on the client.
/// </summary>
[HarmonyPatch(typeof(RunStatisticsDetails), "Initialize")]
public static class RunStatisticsDetailsInitializePatch
{
    [HarmonyPostfix]
    public static void Postfix(RunStatisticsDetails __instance)
    {
        try
        {
            var players = RunStatsSnapshotClientHandler.LatestPerPlayerStats;
            if (players == null || players.Count == 0) return;

            var parentField = AccessTools.Field(typeof(RunStatisticsDetails), "statLinesParent");
            var prefabField = AccessTools.Field(typeof(RunStatisticsDetails), "statLinesPrefab");
            var parent = parentField?.GetValue(__instance) as Transform;
            var prefab = prefabField?.GetValue(__instance) as GameObject;
            if (parent == null || prefab == null) return;

            foreach (var p in players.OrderBy(x => x.SlotIndex))
            {
                var dealtLine = UnityEngine.Object.Instantiate(prefab, parent).GetComponent<StatsLine>();
                dealtLine?.Initialize($"Damage done by {p.PlayerName}", p.DamageDealt.ToString());

                var takenLine = UnityEngine.Object.Instantiate(prefab, parent).GetComponent<StatsLine>();
                takenLine?.Initialize($"Damage taken by {p.PlayerName}", p.DamageTaken.ToString());
            }

            // Restripe the alternating background rows — the native loop ran before
            // our new lines existed, so the added rows default to "enabled image".
            int i = 1;
            foreach (Transform child in parent)
            {
                if (i == 0)
                {
                    var img = child.GetComponentInChildren<UnityEngine.UI.Image>();
                    if (img != null) img.enabled = false;
                }
                i = 1 - i;
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[RunStatsSync] Per-player line injection failed: {ex.Message}");
        }
    }
}
