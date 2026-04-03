using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Battle.Attacks;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Providers;

public class DeckStateProvider : IGameStateProvider<DeckStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly OrbIdentifier _orbId;

    public DeckStateProvider(ManualLogSource log, OrbIdentifier orbId)
    {
        _log = log;
        _orbId = orbId;
    }

    public DeckStateSnapshot Capture()
    {
        try
        {
            var snapshot = new DeckStateSnapshot();

            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm == null) return snapshot;

            var completeDeck = DeckManager.completeDeck;
            if (completeDeck != null)
            {
                for (int i = 0; i < completeDeck.Count; i++)
                {
                    var go = completeDeck[i];
                    if (go == null) continue;
                    var entry = CreateOrbEntry(go);
                    entry.Guid = _orbId.GetOrAssignGuid(go);
                    entry.DeckIndex = i;
                    snapshot.CompleteDeck.Add(entry);
                }
            }

            var battleDeck = dm.battleDeck;
            if (battleDeck != null)
            {
                for (int i = 0; i < battleDeck.Count; i++)
                {
                    var go = battleDeck[i];
                    if (go == null) continue;
                    var entry = CreateOrbEntry(go);
                    entry.Guid = _orbId.GetOrAssignGuid(go);
                    entry.DeckIndex = i;
                    snapshot.BattleDeck.Add(entry);
                }
            }

            // Capture shuffledDeck order (top of stack = first to draw)
            if (dm.shuffledDeck != null && dm.shuffledDeck.Count > 0)
            {
                foreach (var orb in dm.shuffledDeck)
                {
                    if (orb != null)
                        snapshot.ShuffledOrder.Add(orb.name);
                }
            }

            snapshot.DeckSize = snapshot.CompleteDeck.Count;

            // Capture the currently active orb (the one being aimed/fired)
            try
            {
                var bc = UnityEngine.Object.FindObjectOfType<Battle.BattleController>();
                if (bc?.activePachinkoBall != null)
                {
                    snapshot.CurrentOrb = bc.activePachinkoBall.name;
                    var atk = bc.activePachinkoBall.GetComponent<Attack>();
                    snapshot.CurrentOrbLevel = atk?.Level ?? 1;
                }
            }
            catch { }

            _log.LogInfo($"[DeckProvider] Captured {snapshot.CompleteDeck.Count} complete, {snapshot.BattleDeck.Count} battle, {snapshot.ShuffledOrder.Count} shuffled orbs ({_orbId.Count} in registry) activeOrb={snapshot.CurrentOrb ?? "none"}");

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"DeckStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }

    private static OrbEntry CreateOrbEntry(GameObject go)
    {
        var attack = go.GetComponent<Attack>();
        return new OrbEntry
        {
            Name = go.name,
            LocName = attack?.locNameString ?? go.name,
            Level = attack?.Level ?? -1,
            BaseDamage = attack?.DamagePerPeg ?? 0,
            CritDamage = attack?.CritDamagePerPeg ?? 0,
        };
    }
}
