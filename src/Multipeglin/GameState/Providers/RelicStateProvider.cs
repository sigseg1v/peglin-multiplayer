using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.GameState.Snapshots;
using Relics;
using UnityEngine;

namespace Multipeglin.GameState.Providers;

public class RelicStateProvider : IGameStateProvider<RelicStateSnapshot>
{
    private readonly ManualLogSource _log;

    public RelicStateProvider(ManualLogSource log) => _log = log;

    public RelicStateSnapshot Capture()
    {
        try
        {
            var snapshot = new RelicStateSnapshot();

            // RelicManager is a ScriptableObject - FindObjectOfType won't find it
            var rms = Resources.FindObjectsOfTypeAll<RelicManager>();
            var rm = rms.Length > 0 ? rms[0] : null;
            if (rm == null) return snapshot;

            // _ownedRelics is a Dictionary<RelicEffect, Relic>
            var ownedField = AccessTools.Field(typeof(RelicManager), "_ownedRelics");
            var owned = ownedField?.GetValue(rm) as IDictionary<RelicEffect, Relic>;
            if (owned == null) return snapshot;

            // Countdown data
            var countdownField = AccessTools.Field(typeof(RelicManager), "_relicRemainingCountdowns");
            var countdowns = countdownField?.GetValue(rm) as IDictionary<RelicEffect, int>;

            foreach (var kvp in owned)
            {
                var relic = kvp.Value;
                if (relic == null) continue;

                int countdown = 0;
                countdowns?.TryGetValue(kvp.Key, out countdown);

                snapshot.OwnedRelics.Add(new RelicEntry
                {
                    Effect = (int)kvp.Key,
                    EffectName = kvp.Key.ToString(),
                    LocKey = relic.locKey ?? "",
                    Rarity = 0, // relic.rarity may not be public
                    RemainingCountdown = countdown,
                    IsEnabled = true,
                });
            }

            snapshot.TotalRelicCount = snapshot.OwnedRelics.Count;
            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"RelicStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }
}
