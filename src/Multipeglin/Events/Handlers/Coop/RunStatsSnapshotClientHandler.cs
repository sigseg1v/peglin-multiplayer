using System;
using System.Collections.Generic;
using Battle.StatusEffects;
using Challenges;
using HarmonyLib;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Multiplayer;
using PeglinUI.RunSummary;
using Relics;
using Stats;
using Worldmap;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Receives the host's RunStats snapshot + per-player damage tallies and rebuilds
/// StaticGameData.CurrentRunStats on the client so the native RunSummary scene can
/// render. Also stashes the per-player stats for <see cref="Patches.RunSummaryPatches"/>
/// to inject extra StatsLines.
/// </summary>
public sealed class RunStatsSnapshotClientHandler : IClientHandler<RunStatsSnapshotEvent>
{
    /// <summary>
    /// Latest per-player stats, indexed by slot. Populated on the client from the
    /// snapshot and on the host directly in RunSummaryPatches. Consumed by the
    /// RunStatisticsDetails.Initialize postfix.
    /// </summary>
    public static List<PerPlayerStats> LatestPerPlayerStats { get; set; } = new List<PerPlayerStats>();

    public void Handle(RunStatsSnapshotEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || mode.IsHosting)
            {
                return;
            }

            var stats = StaticGameData.CurrentRunStats ?? new RunStats();
            stats.hasWon = e.HasWon;
            stats.hasWonCore = e.HasWonCore;
            stats.isCustomRun = e.IsCustomRun;
            stats.vampireDealTaken = e.VampireDealTaken;
            stats.selectedClass = (Peglin.ClassSystem.Class)e.SelectedClass;
            stats.cruciballLevel = e.CruciballLevel;
            stats.seed = e.Seed ?? "";
            stats.finalHp = e.FinalHp;
            stats.maxHp = e.MaxHp;
            stats.totalDamageDealt = e.TotalDamageDealt;
            stats.coinsEarned = e.CoinsEarned;
            stats.pegsHit = e.PegsHit;
            stats.pegsHitRefresh = e.PegsHitRefresh;
            stats.pegsHitCrit = e.PegsHitCrit;
            stats.bombsThrown = e.BombsThrown;
            stats.bombsThrownRigged = e.BombsThrownRigged;
            stats.runTimerElapsedMilliseconds = e.RunTimerElapsedMs;

            if (DateTime.TryParse(e.EndDateIso, out var endDate))
            {
                stats.endDate = endDate;
            }

            // Prevent the native Stopwatch from adding live-client time on top
            // of the host's final elapsed counter.
            try
            { stats.runTimerSw.Reset(); }
            catch { }

            stats.visitedRooms = new Queue<RoomType>();
            if (e.VisitedRooms != null)
            {
                foreach (var r in e.VisitedRooms)
                {
                    stats.visitedRooms.Enqueue((RoomType)r);
                }
            }

            stats.visitedBosses = new Queue<RunStats.BossType>();
            if (e.VisitedBosses != null)
            {
                foreach (var b in e.VisitedBosses)
                {
                    stats.visitedBosses.Enqueue((RunStats.BossType)b);
                }
            }

            stats.relics = (e.Relics ?? new List<int>()).ConvertAll(r => (RelicEffect)r);
            stats.challenges = (e.Challenges ?? new List<int>()).ConvertAll(c => (ChallengeEffect)c);

            stats.stacksPerStatusEffect = new Dictionary<StatusEffectType, int>();
            if (e.StatusEffectStacks != null)
            {
                for (var i = 0; i < e.StatusEffectStacks.Count; i++)
                {
                    if (e.StatusEffectStacks[i] > 0)
                    {
                        stats.stacksPerStatusEffect[(StatusEffectType)i] = e.StatusEffectStacks[i];
                    }
                }
            }

            stats.slimePegsPerSlimeType = new Dictionary<global::Peg.SlimeType, int>();
            if (e.SlimePegCounts != null)
            {
                for (var i = 0; i < e.SlimePegCounts.Count; i++)
                {
                    if (e.SlimePegCounts[i] > 0)
                    {
                        stats.slimePegsPerSlimeType[(global::Peg.SlimeType)i] = e.SlimePegCounts[i];
                    }
                }
            }

            stats.orbStats = new Dictionary<string, RunStats.OrbPlayData>();
            if (e.Orbs != null)
            {
                foreach (var o in e.Orbs)
                {
                    if (string.IsNullOrEmpty(o.Id))
                    {
                        continue;
                    }

                    stats.orbStats[o.Id] = new RunStats.OrbPlayData
                    {
                        id = o.Id,
                        name = o.Name,
                        damageDealt = o.DamageDealt,
                        timesFired = o.TimesFired,
                        timesDiscarded = o.TimesDiscarded,
                        timesRemoved = o.TimesRemoved,
                        starting = o.Starting,
                        amountInDeck = o.AmountInDeck,
                        levelInstances = o.LevelInstances ?? new int[3],
                    };
                }
            }

            stats.enemyData = new Dictionary<string, RunStats.EnemyPlayData>();
            if (e.Enemies != null)
            {
                foreach (var en in e.Enemies)
                {
                    if (string.IsNullOrEmpty(en.Name))
                    {
                        continue;
                    }

                    stats.enemyData[en.Name] = new RunStats.EnemyPlayData
                    {
                        name = en.Name,
                        amountFought = en.AmountFought,
                        meleeDamageReceived = en.MeleeDamageReceived,
                        rangedDamageReceived = en.RangedDamageReceived,
                        defeatedBy = en.DefeatedBy,
                    };
                }
            }

            StaticGameData.CurrentRunStats = stats;
            LatestPerPlayerStats = e.Players ?? new List<PerPlayerStats>();

            MultiplayerPlugin.Logger?.LogInfo(
                $"[RunStatsSync] Applied snapshot: class={stats.selectedClass}, won={stats.hasWon}, " +
                $"dmg={stats.totalDamageDealt}, pegs={stats.pegsHit}, players={LatestPerPlayerStats.Count}");

            // If the client is already on the RunSummary scene, re-run the init
            // so the lines refresh with the freshly populated stats.
            var rs = UnityEngine.Object.FindObjectOfType<RunSummary>();
            if (rs != null)
            {
                var detailsField = AccessTools.Field(typeof(RunSummary), "runStatisticDetails");
                var details = detailsField?.GetValue(rs) as RunStatisticsDetails;
                if (details != null)
                {
                    try
                    {
                        details.Initialize(stats);
                        MultiplayerPlugin.Logger?.LogInfo("[RunStatsSync] Re-initialized RunStatisticsDetails");
                    }
                    catch (Exception ex)
                    {
                        MultiplayerPlugin.Logger?.LogWarning($"[RunStatsSync] Re-init failed: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[RunStatsSync] Handler failed: {ex.Message}");
        }
    }
}
