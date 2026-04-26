using System;
using System.Collections.Generic;
using System.Linq;
using Battle.Attacks;
using BepInEx.Logging;
using Cruciball;
using HarmonyLib;
using Multipeglin.GameState.Snapshots;
using Relics;
using UnityEngine;

namespace Multipeglin.GameState.Appliers;

public class RelicStateApplier : IGameStateApplier<RelicStateSnapshot>
{
    private readonly ManualLogSource _log;

    public RelicStateApplier(ManualLogSource log) => _log = log;

    public void Apply(RelicStateSnapshot snapshot)
    {
        try
        {
            _log.LogInfo($"[RelicApplier] Syncing {snapshot.TotalRelicCount} relics from host");

            var rms = Resources.FindObjectsOfTypeAll<RelicManager>();
            var rm = rms.Length > 0 ? rms[0] : null;
            if (rm == null)
            {
                _log.LogWarning("[RelicApplier] RelicManager not found");
                return;
            }

            // Get current owned relics
            var ownedField = AccessTools.Field(typeof(RelicManager), "_ownedRelics");
            var owned = ownedField?.GetValue(rm) as Dictionary<RelicEffect, Relic>;
            if (owned == null)
            {
                // RelicManager hasn't been initialized yet — call Reset
                rm.Reset();
                owned = ownedField?.GetValue(rm) as Dictionary<RelicEffect, Relic>;
                if (owned == null)
                {
                    _log.LogWarning("[RelicApplier] Cannot access _ownedRelics after Reset");
                    return;
                }
            }

            // Find all available Relic ScriptableObjects
            var allRelicAssets = Resources.FindObjectsOfTypeAll<Relic>();

            int added = 0, alreadyOwned = 0;
            if (snapshot.OwnedRelics != null)
            {
                foreach (var entry in snapshot.OwnedRelics)
                {
                    var effect = (RelicEffect)entry.Effect;
                    if (owned.ContainsKey(effect))
                    {
                        alreadyOwned++;
                        continue;
                    }

                    // Find the Relic asset by effect or locKey
                    var relicAsset = allRelicAssets.FirstOrDefault(r => r.effect == effect)
                        ?? allRelicAssets.FirstOrDefault(r => r.locKey == entry.LocKey);

                    if (relicAsset != null)
                    {
                        Patches.MultiplayerClientPatches.AllowRelicSync = true;
                        try
                        {
                            rm.AddRelic(relicAsset);
                            added++;
                            _log.LogInfo($"[RelicApplier] Added relic: {entry.EffectName} (loc={entry.LocKey})");
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning($"[RelicApplier] AddRelic failed for {entry.EffectName}: {ex.Message}");
                        }
                        finally
                        {
                            Patches.MultiplayerClientPatches.AllowRelicSync = false;
                        }
                    }
                    else
                    {
                        _log.LogWarning($"[RelicApplier] Relic asset not found: effect={entry.Effect} loc={entry.LocKey} (searched {allRelicAssets.Length} assets)");
                    }
                }
            }

            // In coop mode, do NOT remove the client's own relics. The heartbeat
            // sends the active player's relics which may differ from the client's.
            // Each player keeps their own relics; the sync only adds missing ones.
            var isCoop = UI.LobbyUI.GameStartReceived;
            if (!isCoop)
            {
                // Non-coop (spectator mode): remove relics that host doesn't have
                var hostEffects = new HashSet<RelicEffect>();
                if (snapshot.OwnedRelics != null)
                {
                    foreach (var e in snapshot.OwnedRelics)
                    {
                        hostEffects.Add((RelicEffect)e.Effect);
                    }
                }

                var toRemove = owned.Keys.Where(k => !hostEffects.Contains(k)).ToList();
                foreach (var key in toRemove)
                {
                    owned.Remove(key);
                    _log.LogInfo($"[RelicApplier] Removed extra relic: {key}");
                }
            }

            _log.LogInfo($"[RelicApplier] Result: added={added}, alreadyOwned={alreadyOwned}, total={owned.Count}");

            // Countdown counters (e.g., "X/Y" displays on Trash Can, LIFESTEAL_PEG_HITS,
            // HEAL_ON_PEG_HITS, REFRESH_BUFF, etc.). AddRelic initializes these to the
            // default max value — without this step the client would always show the
            // starting countdown regardless of host state.
            ApplyCountdowns(rm, snapshot);

            // === Post-apply verification ===
            VerifyRelicState(owned, snapshot);

            // Ensure relic UI is up to date — RelicUI subscribes to OnRelicAdded,
            // but relics added before the battle scene loads won't have icons.
            // Find the RelicUI and ensure all owned relics have icons.
            RefreshRelicUI(owned);

            // Re-initialize all orb Attack components with the updated RelicManager
            // so damage displays reflect relic modifiers (e.g., "Suffer the Sling" +1/+2).
            // Without SoftInit, Attack._relicManager is null and CalculateStaticDamageBuffs
            // skips all relic checks → orbs show base damage instead of modified damage.
            if (added > 0)
            {
                ReinitOrbDamageDisplays(rm);
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"[RelicApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Write each host-reported RemainingCountdown into the client's
    /// _relicRemainingCountdowns dict and broadcast OnCountdownDecremented so
    /// the relic UI refreshes. Only writes entries the RelicManager actually
    /// tracks (the effect must be in relicCountdownValues or per-run tables).
    /// </summary>
    private void ApplyCountdowns(RelicManager rm, RelicStateSnapshot snapshot)
    {
        try
        {
            if (snapshot.OwnedRelics == null)
            {
                return;
            }

            var countdownField = AccessTools.Field(typeof(RelicManager), "_relicRemainingCountdowns");
            var perShotField = AccessTools.Field(typeof(RelicManager), "_relicRemainingUsesPerShot");
            var perBattleField = AccessTools.Field(typeof(RelicManager), "_relicRemainingUsesPerBattle");
            var perRunField = AccessTools.Field(typeof(RelicManager), "_relicRemainingUsesPerRun");
            var countdowns = countdownField?.GetValue(rm) as IDictionary<RelicEffect, int>;
            var perShot = perShotField?.GetValue(rm) as IDictionary<RelicEffect, int>;
            var perBattle = perBattleField?.GetValue(rm) as IDictionary<RelicEffect, int>;
            var perRun = perRunField?.GetValue(rm) as IDictionary<RelicEffect, int>;

            // Static "is this effect stackable/countable?" tables. Only relics with
            // entries in these dicts ever show a count number on their icon. Writing
            // to the per-X dicts for unrelated effects causes RelicIcon to display
            // a "0" stack count next to non-stackable relics like Roundreloquence.
            var perBattleStaticField = AccessTools.Field(typeof(RelicManager), "relicUsesPerBattleCounts");
            var perBattleStatic = perBattleStaticField?.GetValue(null) as IDictionary<RelicEffect, int>;
            var hasCountdown = RelicManager.relicCountdownValues;
            var hasPerShot = RelicManager.relicUsesPerShotCounts;
            var hasPerRun = RelicManager.relicUsesPerRunCounts;

            var ownedField = AccessTools.Field(typeof(RelicManager), "_ownedRelics");
            var owned = ownedField?.GetValue(rm) as IDictionary<RelicEffect, Relic>;

            var updated = 0;
            foreach (var entry in snapshot.OwnedRelics)
            {
                var effect = (RelicEffect)entry.Effect;

                // Skip relics the client doesn't actually own yet
                if (owned == null || !owned.ContainsKey(effect))
                {
                    continue;
                }

                var wroteCountdown = false;
                if (hasCountdown != null && hasCountdown.ContainsKey(effect))
                {
                    try
                    {
                        var setter = AccessTools.Method(typeof(RelicManager), "SetRemainingCountdownForRelic");
                        if (setter != null)
                        {
                            setter.Invoke(rm, new object[] { effect, entry.RemainingCountdown });
                            wroteCountdown = true;
                        }
                    }
                    catch
                    {
                    }

                    if (!wroteCountdown && countdowns != null)
                    {
                        countdowns[effect] = entry.RemainingCountdown;
                        wroteCountdown = true;
                    }
                }

                // Per-shot / per-battle / per-run counters: only write for effects
                // that the game actually tracks counters for. Otherwise the UI will
                // show a stale "0" badge on non-stackable relics.
                if (perShot != null && hasPerShot != null && hasPerShot.ContainsKey(effect))
                {
                    perShot[effect] = entry.RemainingUsesPerShot;
                }

                if (perBattle != null && perBattleStatic != null && perBattleStatic.ContainsKey(effect))
                {
                    perBattle[effect] = entry.RemainingUsesPerBattle;
                }

                if (perRun != null && hasPerRun != null && hasPerRun.ContainsKey(effect))
                {
                    perRun[effect] = entry.RemainingUsesPerRun;
                }

                if (wroteCountdown)
                {
                    try
                    {
                        RelicManager.OnCountdownDecremented?.Invoke(owned[effect], entry.RemainingCountdown);
                    }
                    catch
                    {
                    }

                    updated++;
                }
            }

            if (updated > 0)
            {
                _log.LogInfo($"[RelicApplier] Applied countdowns for {updated} relic(s)");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[RelicApplier] ApplyCountdowns failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Post-apply verification: re-read _ownedRelics and compare count with snapshot.
    /// Logs MISMATCH warnings for any differences, INFO on success.
    /// </summary>
    private void VerifyRelicState(Dictionary<RelicEffect, Relic> owned, RelicStateSnapshot snapshot)
    {
        try
        {
            var actualCount = owned?.Count ?? 0;
            var expectedCount = snapshot.OwnedRelics?.Count ?? 0;

            if (actualCount != expectedCount)
            {
                _log.LogWarning($"[Verify] MISMATCH relics: actual={actualCount} expected={expectedCount}");

                // Log which relics are missing or extra for debugging
                if (snapshot.OwnedRelics != null && owned != null)
                {
                    var expectedEffects = new HashSet<RelicEffect>();
                    foreach (var entry in snapshot.OwnedRelics)
                    {
                        expectedEffects.Add((RelicEffect)entry.Effect);
                    }

                    foreach (var effect in expectedEffects)
                    {
                        if (!owned.ContainsKey(effect))
                        {
                            _log.LogWarning($"[Verify]   MISSING relic: {effect}");
                        }
                    }

                    foreach (var effect in owned.Keys)
                    {
                        if (!expectedEffects.Contains(effect))
                        {
                            _log.LogWarning($"[Verify]   EXTRA relic: {effect}");
                        }
                    }
                }
            }
            else
            {
                _log.LogInfo($"[Verify] RelicState OK: count={actualCount}");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[Verify] RelicState verification failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensure all owned relics have UI icons. RelicUI only creates icons when
    /// OnRelicAdded fires, but relics added before the scene loads miss this.
    /// </summary>
    private void RefreshRelicUI(Dictionary<RelicEffect, Relic> owned)
    {
        try
        {
            var relicUI = UnityEngine.Object.FindObjectOfType<RelicUI>();
            if (relicUI == null)
            {
                return;
            }

            var iconsField = AccessTools.Field(typeof(RelicUI), "icons");
            var icons = iconsField?.GetValue(relicUI) as Dictionary<RelicEffect, RelicIcon>;
            if (icons == null)
            {
                return;
            }

            var refreshed = 0;
            foreach (var kvp in owned)
            {
                if (!icons.ContainsKey(kvp.Key) && kvp.Value != null)
                {
                    // Invoke OnRelicAdded to create the icon
                    RelicManager.OnRelicAdded?.Invoke(kvp.Value);
                    refreshed++;
                }
            }

            if (refreshed > 0)
            {
                _log.LogInfo($"[RelicApplier] Refreshed {refreshed} relic icons in UI");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[RelicApplier] RefreshRelicUI failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Call SoftInit on all Attack components in the scene and deck so their
    /// damage displays reflect the current relic modifiers. Without this,
    /// Attack._relicManager stays null and CalculateStaticDamageBuffs returns
    /// base damage (e.g., Pebbal shows 2/4 instead of 3/6 with Suffer the Sling).
    /// </summary>
    private void ReinitOrbDamageDisplays(RelicManager rm)
    {
        try
        {
            var dm = Resources.FindObjectsOfTypeAll<DeckManager>();
            var deckManager = dm.Length > 0 ? dm[0] : null;

            var cms = Resources.FindObjectsOfTypeAll<CruciballManager>();
            var cruciballManager = cms.Length > 0 ? cms[0] : null;

            // Re-init all Attack components in the scene (active orbs, UI displays)
            var attacks = UnityEngine.Object.FindObjectsOfType<Attack>(true);
            var count = 0;
            foreach (var atk in attacks)
            {
                try
                {
                    atk.SoftInit(deckManager, rm, cruciballManager);
                    count++;
                }
                catch
                {
                }
            }

            // Also re-init orbs in the complete deck (they're GameObjects,
            // some may not be in the scene so FindObjectsOfType misses them)
            if (DeckManager.completeDeck != null)
            {
                foreach (var orbGo in DeckManager.completeDeck)
                {
                    if (orbGo == null)
                    {
                        continue;
                    }

                    var atk = orbGo.GetComponent<Attack>();
                    if (atk != null)
                    {
                        try
                        {
                            atk.SoftInit(deckManager, rm, cruciballManager);
                            count++;
                        }
                        catch
                        {
                        }
                    }
                }
            }

            _log.LogInfo($"[RelicApplier] Re-initialized {count} Attack components with relic modifiers");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[RelicApplier] ReinitOrbDamageDisplays failed: {ex.Message}");
        }
    }
}
