using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Battle.Attacks;
using PeglinMods.Multiplayer.GameState.Snapshots;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Providers;

public class DeckStateProvider : IGameStateProvider<DeckStateSnapshot>
{
    private readonly ManualLogSource _log;

    public DeckStateProvider(ManualLogSource log) => _log = log;

    public DeckStateSnapshot Capture()
    {
        try
        {
            var snapshot = new DeckStateSnapshot();

            // DeckManager is a ScriptableObject - FindObjectOfType won't find it
            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm == null) return snapshot;

            // completeDeck is a public static field
            var completeDeck = DeckManager.completeDeck;
            if (completeDeck != null)
            {
                foreach (var go in completeDeck)
                {
                    if (go == null) continue;
                    snapshot.CompleteDeck.Add(CreateOrbEntry(go));
                }
            }

            // battleDeck is a public instance field
            var battleDeck = dm.battleDeck;
            if (battleDeck != null)
            {
                foreach (var go in battleDeck)
                {
                    if (go == null) continue;
                    snapshot.BattleDeck.Add(CreateOrbEntry(go));
                }
            }

            snapshot.DeckSize = snapshot.CompleteDeck.Count;

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
