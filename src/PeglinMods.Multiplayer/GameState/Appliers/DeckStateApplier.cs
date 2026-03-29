using System;
using System.Collections.Generic;
using Battle.Attacks;
using BepInEx.Logging;
using Loading;
using PeglinMods.Multiplayer.GameState.Snapshots;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

public class DeckStateApplier : IGameStateApplier<DeckStateSnapshot>
{
    private readonly ManualLogSource _log;

    public DeckStateApplier(ManualLogSource log) => _log = log;

    public void Apply(DeckStateSnapshot snapshot)
    {
        try
        {
            _log.LogInfo($"[DeckApplier] Syncing deck: {snapshot.DeckSize} complete, {snapshot.BattleDeck?.Count ?? 0} battle orbs");

            // Find DeckManager (ScriptableObject — not in scene hierarchy)
            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm == null)
            {
                _log.LogWarning("[DeckApplier] DeckManager not found");
                return;
            }

            // Sync complete deck
            if (snapshot.CompleteDeck != null && snapshot.CompleteDeck.Count > 0)
            {
                SyncCompleteDeck(dm, snapshot.CompleteDeck);
            }

            // Sync battle deck
            if (snapshot.BattleDeck != null && snapshot.BattleDeck.Count > 0)
            {
                SyncBattleDeck(dm, snapshot.BattleDeck);
            }

            _log.LogInfo($"[DeckApplier] Deck sync complete: completeDeck={DeckManager.completeDeck?.Count ?? 0}, battleDeck={dm.battleDeck?.Count ?? 0}");
        }
        catch (Exception ex)
        {
            _log.LogError($"[DeckApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void SyncCompleteDeck(DeckManager dm, List<OrbEntry> hostDeck)
    {
        var completeDeck = DeckManager.completeDeck;
        if (completeDeck == null)
        {
            DeckManager.completeDeck = new List<GameObject>();
            completeDeck = DeckManager.completeDeck;
        }

        // Check if deck already matches (same count and names)
        if (completeDeck.Count == hostDeck.Count)
        {
            bool match = true;
            for (int i = 0; i < hostDeck.Count; i++)
            {
                if (i >= completeDeck.Count || completeDeck[i] == null) { match = false; break; }
                var name = completeDeck[i].GetComponent<Attack>()?.locNameString ?? completeDeck[i].name;
                if (name != hostDeck[i].LocName && completeDeck[i].name != hostDeck[i].Name) { match = false; break; }
            }
            if (match)
            {
                _log.LogInfo("[DeckApplier] Complete deck already matches host");
                return;
            }
        }

        // Rebuild complete deck from host data
        int loaded = 0;
        var newDeck = new List<GameObject>();
        foreach (var entry in hostDeck)
        {
            try
            {
                // Find orb prefab by name from AssetLoading cache
                var orbGo = AssetLoading.Instance?.GetOrbPrefab(entry.Name);
                if (orbGo != null)
                {
                    var instance = UnityEngine.Object.Instantiate(orbGo);
                    instance.name = entry.Name;
                    instance.SetActive(false);
                    newDeck.Add(instance);
                    loaded++;
                }
                else
                {
                    _log.LogWarning($"[DeckApplier] Orb prefab not found: '{entry.Name}'");
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning($"[DeckApplier] Failed to load orb '{entry.Name}': {ex.Message}");
            }
        }

        // Replace the deck
        // Destroy old orb instances
        foreach (var go in completeDeck)
        {
            if (go != null) UnityEngine.Object.Destroy(go);
        }
        completeDeck.Clear();
        completeDeck.AddRange(newDeck);

        _log.LogInfo($"[DeckApplier] Rebuilt complete deck: {loaded}/{hostDeck.Count} orbs loaded");
    }

    private void SyncBattleDeck(DeckManager dm, List<OrbEntry> hostBattleDeck)
    {
        if (dm.battleDeck == null)
        {
            dm.battleDeck = new List<GameObject>();
        }

        // Only rebuild if counts differ
        if (dm.battleDeck.Count == hostBattleDeck.Count) return;

        int loaded = 0;
        var newBattleDeck = new List<GameObject>();
        foreach (var entry in hostBattleDeck)
        {
            try
            {
                var orbGo = AssetLoading.Instance?.GetOrbPrefab(entry.Name);
                if (orbGo != null)
                {
                    var instance = UnityEngine.Object.Instantiate(orbGo);
                    instance.name = entry.Name;
                    instance.SetActive(false);
                    newBattleDeck.Add(instance);
                    loaded++;
                }
            }
            catch { }
        }

        foreach (var go in dm.battleDeck)
        {
            if (go != null) UnityEngine.Object.Destroy(go);
        }
        dm.battleDeck.Clear();
        dm.battleDeck.AddRange(newBattleDeck);

        _log.LogInfo($"[DeckApplier] Rebuilt battle deck: {loaded}/{hostBattleDeck.Count} orbs");
    }
}
