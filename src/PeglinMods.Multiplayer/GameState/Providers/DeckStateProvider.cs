using System;
using BepInEx.Logging;
using HarmonyLib;
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

            // DeckManager is a ScriptableObject - find via scene references
            var dm = UnityEngine.Object.FindObjectOfType<DeckManager>();
            if (dm == null) return snapshot;

            // Complete deck
            var completeDeckField = AccessTools.Field(typeof(DeckManager), "completeDeck")
                ?? AccessTools.Field(typeof(DeckManager), "_completeDeck");
            var completeDeck = completeDeckField?.GetValue(dm) as System.Collections.IList;
            if (completeDeck != null)
            {
                foreach (var orbObj in completeDeck)
                {
                    var go = orbObj as GameObject;
                    if (go == null) continue;
                    snapshot.CompleteDeck.Add(CreateOrbEntry(go));
                }
            }

            // Battle deck (the shuffled draw pile)
            var battleDeckField = AccessTools.Field(typeof(DeckManager), "battleDeck")
                ?? AccessTools.Field(typeof(DeckManager), "_battleDeck");
            var battleDeck = battleDeckField?.GetValue(dm) as System.Collections.IList;
            if (battleDeck != null)
            {
                foreach (var orbObj in battleDeck)
                {
                    var go = orbObj as GameObject;
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
