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

            // Countdown / per-shot / per-battle / per-run counters. RelicManager
            // tracks each in its own dict; we capture all four so the client can
            // mirror "X / Y" displays (Slimy Salve, Tipped Pegs, Dive Reload, etc.).
            var countdownField = AccessTools.Field(typeof(RelicManager), "_relicRemainingCountdowns");
            var perShotField = AccessTools.Field(typeof(RelicManager), "_relicRemainingUsesPerShot");
            var perBattleField = AccessTools.Field(typeof(RelicManager), "_relicRemainingUsesPerBattle");
            var perRunField = AccessTools.Field(typeof(RelicManager), "_relicRemainingUsesPerRun");
            var countdowns = countdownField?.GetValue(rm) as IDictionary<RelicEffect, int>;
            var perShot = perShotField?.GetValue(rm) as IDictionary<RelicEffect, int>;
            var perBattle = perBattleField?.GetValue(rm) as IDictionary<RelicEffect, int>;
            var perRun = perRunField?.GetValue(rm) as IDictionary<RelicEffect, int>;

            foreach (var kvp in owned)
            {
                var relic = kvp.Value;
                if (relic == null) continue;

                int countdown = 0, ps = 0, pb = 0, pr = 0;
                countdowns?.TryGetValue(kvp.Key, out countdown);
                perShot?.TryGetValue(kvp.Key, out ps);
                perBattle?.TryGetValue(kvp.Key, out pb);
                perRun?.TryGetValue(kvp.Key, out pr);

                snapshot.OwnedRelics.Add(new RelicEntry
                {
                    Effect = (int)kvp.Key,
                    EffectName = kvp.Key.ToString(),
                    LocKey = relic.locKey ?? "",
                    Rarity = 0, // relic.rarity may not be public
                    RemainingCountdown = countdown,
                    RemainingUsesPerShot = ps,
                    RemainingUsesPerBattle = pb,
                    RemainingUsesPerRun = pr,
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
