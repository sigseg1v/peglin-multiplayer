using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using PeglinMods.Multiplayer.GameState.Snapshots;
using Relics;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

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
                    }
                    else
                    {
                        _log.LogWarning($"[RelicApplier] Relic asset not found: effect={entry.Effect} loc={entry.LocKey} (searched {allRelicAssets.Length} assets)");
                    }
                }
            }

            // Remove relics that host doesn't have
            var hostEffects = new HashSet<RelicEffect>();
            if (snapshot.OwnedRelics != null)
                foreach (var e in snapshot.OwnedRelics)
                    hostEffects.Add((RelicEffect)e.Effect);

            var toRemove = owned.Keys.Where(k => !hostEffects.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                owned.Remove(key);
                _log.LogInfo($"[RelicApplier] Removed extra relic: {key}");
            }

            _log.LogInfo($"[RelicApplier] Result: added={added}, alreadyOwned={alreadyOwned}, removed={toRemove.Count}, total={owned.Count}");
        }
        catch (Exception ex)
        {
            _log.LogError($"[RelicApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
