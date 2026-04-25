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
            var entry = new PerPlayerStats
            {
                SlotIndex = state.SlotIndex,
                PlayerName = string.IsNullOrEmpty(state.PlayerName) ? $"Player {state.SlotIndex + 1}" : state.PlayerName,
                DamageDealt = state.DamageDealt,
                DamageTaken = state.DamageTaken,
                ChosenClass = state.ChosenClass,
                FinalHp = Mathf.Max(0, Mathf.RoundToInt(state.CurrentHealth)),
                MaxHp = Mathf.Max(0, Mathf.RoundToInt(state.MaxHealth)),
                Gold = state.Gold,
                IsAlive = state.CurrentHealth > 0,
            };

            if (state.OwnedRelics != null)
                foreach (var r in state.OwnedRelics) entry.Relics.Add(r.Effect);

            if (state.CompleteDeck != null)
                foreach (var o in state.CompleteDeck)
                    entry.Orbs.Add(new PerPlayerOrb { PrefabName = o.PrefabName, Level = o.Level });

            result.Add(entry);
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
/// Re-skins the run statistics details panel per-player so each player's own
/// orbs, relics, header, HP, gold, and damage lines are visible on their own
/// page. LoadMainMenu is intercepted to cycle pages until the final player,
/// then lets the native "return to main menu" run.
/// </summary>
[HarmonyPatch(typeof(RunStatisticsDetails), "Initialize")]
public static class RunStatisticsDetailsInitializePatch
{
    /// <summary>Which player's page is being shown. Reset whenever a new summary opens.</summary>
    public static int CurrentPageIndex = 0;

    [HarmonyPostfix]
    public static void Postfix(RunStatisticsDetails __instance, RunStats stats)
    {
        try
        {
            var players = RunStatsSnapshotClientHandler.LatestPerPlayerStats;
            if (players == null || players.Count == 0) return;

            // Keep pages ordered by slot index so page 1 is always host.
            var ordered = players.OrderBy(p => p.SlotIndex).ToList();

            if (CurrentPageIndex < 0) CurrentPageIndex = 0;
            if (CurrentPageIndex >= ordered.Count) CurrentPageIndex = ordered.Count - 1;

            var current = ordered[CurrentPageIndex];
            RenderPerPlayerPage(__instance, stats, current, CurrentPageIndex, ordered.Count);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[RunStatsSync] Per-player page render failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void RenderPerPlayerPage(
        RunStatisticsDetails inst, RunStats stats, PerPlayerStats player, int pageIndex, int pageCount)
    {
        // --- Header: "<PlayerName> — <Class> - <Win/Loss> (X/Y)" ---
        var headerField = AccessTools.Field(typeof(RunStatisticsDetails), "headerText");
        if (headerField?.GetValue(inst) is TMPro.TextMeshProUGUI header)
        {
            string classLocKey = FindClassLocKey(inst, player.ChosenClass);
            string className = !string.IsNullOrEmpty(classLocKey)
                ? I2.Loc.LocalizationManager.GetTranslation(classLocKey)
                : "";
            string result = stats.hasWon
                ? I2.Loc.LocalizationManager.GetTranslation("Menu/RunSummary/win_label")
                : player.IsAlive
                    ? I2.Loc.LocalizationManager.GetTranslation("Menu/RunSummary/loss_label")
                    : I2.Loc.LocalizationManager.GetTranslation("Menu/RunSummary/loss_label");
            string pageSuffix = pageCount > 1 ? $"   ({pageIndex + 1}/{pageCount})" : "";
            header.text = $"{player.PlayerName} — {className} - {result}{pageSuffix}";
        }

        // --- Override Final HP / Gold stat lines to the current player's values ---
        var parent = AccessTools.Field(typeof(RunStatisticsDetails), "statLinesParent")?.GetValue(inst) as Transform;
        if (parent != null)
        {
            ReplaceStatLineValue(parent, "Menu/RunSummary/final_hp", $"{player.FinalHp} / {player.MaxHp}");
            ReplaceStatLineValue(parent, "Menu/RunSummary/gold_obtained", $"{player.Gold}");

            // Remove any damage lines from previous page renders (we add our own).
            RemoveStatLinesWithLabelPrefix(parent, "Damage done by ");
            RemoveStatLinesWithLabelPrefix(parent, "Damage taken by ");

            var prefab = AccessTools.Field(typeof(RunStatisticsDetails), "statLinesPrefab")?.GetValue(inst) as GameObject;
            if (prefab != null)
            {
                var dealtLine = UnityEngine.Object.Instantiate(prefab, parent).GetComponent<StatsLine>();
                dealtLine?.Initialize($"Damage done by {player.PlayerName}", player.DamageDealt.ToString());

                var takenLine = UnityEngine.Object.Instantiate(prefab, parent).GetComponent<StatsLine>();
                takenLine?.Initialize($"Damage taken by {player.PlayerName}", player.DamageTaken.ToString());
            }

            RestripeRows(parent);
        }

        // --- Orbs: replace with this player's run-end loadout ---
        var orbsParent = AccessTools.Field(typeof(RunStatisticsDetails), "orbsParent")?.GetValue(inst) as Transform;
        var orbsCarousel = AccessTools.Field(typeof(RunStatisticsDetails), "orbsCarousel")?.GetValue(inst) as PeglinUI.LoadoutManager.ImageCarousel;
        var orbPrefab = AccessTools.Field(typeof(RunStatisticsDetails), "loadoutOrbPrefab")?.GetValue(inst) as GameObject;
        if (orbsParent != null && orbsCarousel != null && orbPrefab != null)
        {
            // Destroy any orb GameObjects the native CreateOrbs instantiated under orbsParent.
            for (int i = orbsParent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(orbsParent.GetChild(i).gameObject);
            orbsCarousel.ClearElements();

            var list = new List<GameObject>();
            foreach (var orb in player.Orbs)
            {
                if (string.IsNullOrEmpty(orb.PrefabName)) continue;
                var orbGO = Loading.AssetLoading.Instance?.GetOrbPrefab(orb.PrefabName);
                if (orbGO == null)
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[RunStatsSync] Orb prefab not found: {orb.PrefabName}");
                    continue;
                }
                var icon = UnityEngine.Object.Instantiate(orbPrefab, orbsParent).GetComponent<PeglinUI.LoadoutManager.LoadoutIcon>();
                if (icon == null) continue;
                icon.InitializeOrb(orbGO, Mathf.Clamp(orb.Level, 0, 2));
                ForceIconUnlocked(icon, orbGO, null);
                list.Add(icon.gameObject);
            }
            orbsCarousel.Initialize(list);
        }

        // --- Relics: replace with this player's owned relics ---
        var relicsParent = AccessTools.Field(typeof(RunStatisticsDetails), "relicsParent")?.GetValue(inst) as Transform;
        var relicsCarousel = AccessTools.Field(typeof(RunStatisticsDetails), "relicsCarousel")?.GetValue(inst) as PeglinUI.LoadoutManager.ImageCarousel;
        var relicPrefab = AccessTools.Field(typeof(RunStatisticsDetails), "loadoutRelicsPrefab")?.GetValue(inst) as GameObject;
        var relicMgr = AccessTools.Field(typeof(RunStatisticsDetails), "relicManager")?.GetValue(inst) as Relics.RelicManager;
        if (relicsParent != null && relicsCarousel != null && relicPrefab != null && relicMgr != null)
        {
            for (int i = relicsParent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(relicsParent.GetChild(i).gameObject);
            relicsCarousel.ClearElements();

            var list = new List<GameObject>();
            foreach (var effectInt in player.Relics)
            {
                var relic = relicMgr.GetRelicForEffectFromAllData((Relics.RelicEffect)effectInt);
                if (relic == null)
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[RunStatsSync] Relic not found for effect: {(Relics.RelicEffect)effectInt}");
                    continue;
                }
                var icon = UnityEngine.Object.Instantiate(relicPrefab, relicsParent).GetComponent<PeglinUI.LoadoutManager.LoadoutIcon>();
                if (icon == null) continue;
                icon.InitializeRelic(relic);
                ForceIconUnlocked(icon, null, relic);
                list.Add(icon.gameObject);
            }
            relicsCarousel.Initialize(list);
        }
    }

    /// <summary>
    /// LoadoutIcon.InitializeOrb/InitializeRelic check the local client's
    /// PersistentPlayerData unlock list and gray-out (and disable click) anything
    /// the local client has never personally unlocked. In coop the run summary
    /// shows other players' loadouts — we must show them as fully visible
    /// regardless of this client's unlock history. Restore the correct color
    /// and clear the desaturation UIEffect.
    /// </summary>
    private static void ForceIconUnlocked(PeglinUI.LoadoutManager.LoadoutIcon icon, GameObject orbPrefabOrNull, Relics.Relic relicOrNull)
    {
        try
        {
            icon.isUnlocked = true;
            if (icon.image != null)
            {
                if (orbPrefabOrNull != null)
                {
                    var visuals = PeglinUI.UIUtils.OrbUIUtils.GetOrbVisuals(orbPrefabOrNull);
                    icon.image.color = visuals.color;
                }
                else
                {
                    icon.image.color = Color.white;
                }

                // Zero out the desaturation UIEffect (Coffee.UIEffects.UIEffect.colorFactor).
                var components = icon.image.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var t = comp.GetType();
                    if (t.Name != "UIEffect") continue;
                    var prop = t.GetProperty("colorFactor");
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(comp, 0f, null);
                        break;
                    }
                    var field = t.GetField("colorFactor");
                    if (field != null)
                    {
                        field.SetValue(comp, 0f);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[RunStatsSync] ForceIconUnlocked failed: {ex.Message}");
        }
    }

    private static string FindClassLocKey(RunStatisticsDetails inst, int chosenClass)
    {
        try
        {
            var classInfos = inst.classInfos;
            if (classInfos == null) return null;
            foreach (var ci in classInfos)
            {
                if ((int)ci.characterClass == chosenClass) return ci.classNameLocKey;
            }
        }
        catch { }
        return null;
    }

    private static void ReplaceStatLineValue(Transform parent, string labelLocKey, string newValue)
    {
        try
        {
            string label = I2.Loc.LocalizationManager.GetTranslation(labelLocKey);
            if (string.IsNullOrEmpty(label)) return;

            foreach (Transform child in parent)
            {
                var sl = child.GetComponent<StatsLine>();
                if (sl == null) continue;
                var texts = child.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
                if (texts == null || texts.Length < 2) continue;
                if (texts[0].text == label)
                {
                    texts[1].text = newValue;
                    return;
                }
            }
        }
        catch { }
    }

    private static void RemoveStatLinesWithLabelPrefix(Transform parent, string prefix)
    {
        var toRemove = new List<GameObject>();
        foreach (Transform child in parent)
        {
            var texts = child.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            if (texts != null && texts.Length > 0 && texts[0].text != null && texts[0].text.StartsWith(prefix))
                toRemove.Add(child.gameObject);
        }
        foreach (var go in toRemove) UnityEngine.Object.DestroyImmediate(go);
    }

    private static void RestripeRows(Transform parent)
    {
        int i = 1;
        foreach (Transform child in parent)
        {
            var img = child.GetComponentInChildren<UnityEngine.UI.Image>();
            if (img != null) img.enabled = i != 0;
            i = 1 - i;
        }
    }
}

/// <summary>
/// Intercepts RunSummary.LoadMainMenu. If there are more per-player pages to
/// show, advance to the next one and re-initialize the details panel. Only on
/// the last page do we let the native "return to main menu" run.
/// </summary>
[HarmonyPatch(typeof(RunSummary), "LoadMainMenu")]
public static class RunSummaryLoadMainMenuPatch
{
    [HarmonyPrefix]
    public static bool Prefix(RunSummary __instance)
    {
        try
        {
            var players = RunStatsSnapshotClientHandler.LatestPerPlayerStats;
            if (players == null || players.Count <= 1) return true;

            int next = RunStatisticsDetailsInitializePatch.CurrentPageIndex + 1;
            if (next >= players.Count)
            {
                // Reset for the next run so the summary always opens at page 1.
                RunStatisticsDetailsInitializePatch.CurrentPageIndex = 0;
                return true;
            }

            RunStatisticsDetailsInitializePatch.CurrentPageIndex = next;

            var detailsField = AccessTools.Field(typeof(RunSummary), "runStatisticDetails");
            var details = detailsField?.GetValue(__instance) as RunStatisticsDetails;
            var stats = StaticGameData.CurrentRunStats;
            if (details != null && stats != null)
            {
                details.Initialize(stats);
                MultiplayerPlugin.Logger?.LogInfo($"[RunStatsSync] Advanced to player page {next + 1}/{players.Count}");
            }
            return false;
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[RunStatsSync] LoadMainMenu page advance failed: {ex.Message}");
            return true;
        }
    }
}

/// <summary>
/// Reset the page index every time the RunSummary screen opens, so re-entering
/// (e.g. after a new run) always starts at page 1.
/// </summary>
[HarmonyPatch(typeof(RunSummary), "OnEnable")]
public static class RunSummaryOnEnableResetPagePatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        RunStatisticsDetailsInitializePatch.CurrentPageIndex = 0;
    }
}
