using System;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Multipeglin.Events.Subscriptions.Coop;

/// <summary>
/// Default implementation of ICoopDeckManager. Operates on the live DeckManager
/// (located via Resources.FindObjectsOfTypeAll because it's a ScriptableObject)
/// and PlayerStatusEffectController in the active scene.
/// </summary>
internal sealed class CoopDeckManager : ICoopDeckManager
{
    private readonly ManualLogSource _log;

    public CoopDeckManager(ManualLogSource log)
    {
        _log = log;
    }

    /// <summary>
    /// After SwapToPlayer loads deck state, only shuffle if truly needed.
    /// LoadDeckState rebuilds battleDeck and shuffledDeck from the saved state.
    /// If shuffledDeck is populated, the loaded order is authoritative — do NOT
    /// re-shuffle or the host-deterministic draw order will be lost.
    /// Only shuffle if both battleDeck and shuffledDeck are empty (no state was saved),
    /// or if battleDeck has orbs but shuffledDeck couldn't be rebuilt (name mismatch).
    /// </summary>
    public bool EnsureBattleDeckPopulated(string context)
    {
        try
        {
            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            if (dms == null || dms.Length == 0)
            {
                return false;
            }

            var dm = dms[0];
            var hasBattle = dm.battleDeck != null && dm.battleDeck.Count > 0;
            var hasShuffled = dm.shuffledDeck != null && dm.shuffledDeck.Count > 0;

            if (hasBattle && hasShuffled)
            {
                // Both populated from loaded state — do NOT re-shuffle
                return true;
            }

            if (hasBattle && !hasShuffled)
            {
                // Battle deck loaded but shuffled order couldn't be rebuilt
                // (e.g. orb name matching failed). Re-shuffle from the loaded battle deck.
                _log.LogInfo($"[CoopSubs] {context}: battleDeck has {dm.battleDeck.Count} orbs but shuffledDeck empty — re-shuffling");
                dm.ShuffleBattleDeck();
            }
            else if (!hasBattle && DeckManager.completeDeck != null && DeckManager.completeDeck.Count > 0)
            {
                // No battle deck loaded but complete deck exists — initialize battle deck.
                // CRITICAL: ShuffleBattleDeck() calls ShuffleCompleteDeck(fromComplete: false)
                // which reads from the EMPTY battleDeck and does nothing.
                // We must call ShuffleCompleteDeck(fromComplete: true) to build battleDeck
                // from completeDeck and populate shuffledDeck.
                _log.LogInfo($"[CoopSubs] {context}: battleDeck empty, initializing from completeDeck ({DeckManager.completeDeck.Count} orbs)");

                var shuffleMethod = AccessTools.Method(typeof(DeckManager), "ShuffleCompleteDeck", new[] { typeof(bool) });
                if (shuffleMethod != null)
                {
                    shuffleMethod.Invoke(dm, new object[] { true });
                    _log.LogInfo($"[CoopSubs] {context}: after ShuffleCompleteDeck(true): battleDeck={dm.battleDeck?.Count ?? 0}, shuffledDeck={dm.shuffledDeck?.Count ?? 0}");
                }
                else
                {
                    // Fallback: manually copy completeDeck into battleDeck, then shuffle
                    _log.LogWarning($"[CoopSubs] {context}: ShuffleCompleteDeck not found, manually copying completeDeck to battleDeck");
                    if (dm.battleDeck == null)
                    {
                        dm.battleDeck = new System.Collections.Generic.List<GameObject>();
                    }

                    foreach (var orb in DeckManager.completeDeck)
                    {
                        if (orb != null)
                        {
                            var instance = UnityEngine.Object.Instantiate(orb);
                            instance.name = orb.name;
                            instance.SetActive(false);
                            dm.battleDeck.Add(instance);
                        }
                    }

                    dm.ShuffleBattleDeck();
                    _log.LogInfo($"[CoopSubs] {context}: after fallback shuffle: battleDeck={dm.battleDeck?.Count ?? 0}, shuffledDeck={dm.shuffledDeck?.Count ?? 0}");
                }
            }
            // else: both empty — nothing to do, likely pre-battle state

            // Return success: both battleDeck and shuffledDeck are non-empty
            var success = (dm.battleDeck != null && dm.battleDeck.Count > 0) &&
                           (dm.shuffledDeck != null && dm.shuffledDeck.Count > 0);
            return success;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] EnsureBattleDeckPopulated ({context}) failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Mirror BattleController's native ApplyStartingBonuses for a non-host player
    /// at battle init. Native code only runs for the host (the slot loaded into the
    /// singletons when BattleController.Start() executes), so non-host relics like
    /// Spiral Slayer (START_WITH_STR) never grant their starting status effect.
    ///
    /// Call this AFTER SwapToPlayer(slot) loads the slot's relics into the singleton
    /// and BEFORE SaveActivePlayerState() captures the resulting status effects.
    /// We also reset per-battle relic counters here so the bonus actually applies on
    /// every battle (AttemptUseRelic decrements the counter each call).
    /// </summary>
    public void ApplyNonHostStartingBonuses(int slot)
    {
        try
        {
            var relicMgr = Resources.FindObjectsOfTypeAll<Relics.RelicManager>()?.FirstOrDefault();
            if (relicMgr != null)
            {
                try
                {
                    relicMgr.ResetBattleRelics();
                }
                catch (Exception rex) { _log.LogWarning($"[CoopSubs] ResetBattleRelics for slot {slot} failed: {rex.Message}"); }
            }

            var psec = UnityEngine.Object.FindObjectOfType<Battle.StatusEffects.PlayerStatusEffectController>();
            if (psec == null)
            {
                _log.LogWarning($"[CoopSubs] ApplyNonHostStartingBonuses: PlayerStatusEffectController not found for slot {slot}");
                return;
            }

            psec.ApplyStartingBonuses();
            _log.LogInfo($"[CoopSubs] Applied starting bonuses for slot {slot}");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[CoopSubs] ApplyNonHostStartingBonuses for slot {slot} failed: {ex.Message}");
        }
    }
}
