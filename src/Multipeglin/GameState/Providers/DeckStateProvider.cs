using System;
using Battle.Attacks;
using BepInEx.Logging;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.GameState.Providers;

public class DeckStateProvider : IGameStateProvider<DeckStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly OrbIdentifier _orbId;

    public DeckStateProvider(ManualLogSource log, OrbIdentifier orbId)
    {
        _log = log;
        _orbId = orbId;
    }

    /// <summary>
    /// Capture the current deck state from DeckManager singletons.
    /// NOTE: ActiveSlotIndex is NOT set here -- it is populated by the caller
    /// (GameStateSyncService.SyncAll) which has access to CoopStateManager's
    /// active slot. The provider intentionally does not depend on CoopStateManager.
    /// </summary>
    public DeckStateSnapshot Capture()
    {
        try
        {
            var snapshot = new DeckStateSnapshot();

            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm == null)
            {
                return snapshot;
            }

            var completeDeck = DeckManager.completeDeck;
            if (completeDeck != null)
            {
                for (var i = 0; i < completeDeck.Count; i++)
                {
                    var go = completeDeck[i];
                    if (go == null)
                    {
                        continue;
                    }

                    var entry = CreateOrbEntry(go);
                    entry.Guid = _orbId.GetOrAssignGuid(go);
                    entry.DeckIndex = i;
                    snapshot.CompleteDeck.Add(entry);
                }
            }

            var battleDeck = dm.battleDeck;
            if (battleDeck != null)
            {
                for (var i = 0; i < battleDeck.Count; i++)
                {
                    var go = battleDeck[i];
                    if (go == null)
                    {
                        continue;
                    }

                    var entry = CreateOrbEntry(go);
                    entry.Guid = _orbId.GetOrAssignGuid(go);
                    entry.DeckIndex = i;
                    snapshot.BattleDeck.Add(entry);
                }
            }

            // Capture shuffledDeck order (top of stack = first to draw).
            // Null vs empty semantics for downstream consumers:
            // - ShuffledOrder = null  -> no data available (shuffledDeck uninitialized), use fallback
            // - ShuffledOrder = []    -> deck truly empty (all orbs drawn as active)
            // - ShuffledOrder = [...] -> populated with GUIDs in draw order
            if (dm.shuffledDeck == null)
            {
                snapshot.ShuffledOrder = null;
            }
            else if (dm.shuffledDeck.Count > 0)
            {
                foreach (var orb in dm.shuffledDeck)
                {
                    if (orb != null)
                    {
                        snapshot.ShuffledOrder.Add(_orbId.GetOrAssignGuid(orb));
                    }
                }
            }
            // else: shuffledDeck.Count == 0 -> ShuffledOrder stays as initialized empty list

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
            catch (Exception activeOrbEx) { _log.LogWarning($"[DeckProvider] Failed to capture active orb: {activeOrbEx.Message}"); }

            _log.LogInfo($"[DeckProvider] Captured {snapshot.CompleteDeck.Count} complete, {snapshot.BattleDeck.Count} battle, {snapshot.ShuffledOrder?.Count ?? 0} shuffled orbs ({_orbId.Count} in registry) activeOrb={snapshot.CurrentOrb ?? "none"}");

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
