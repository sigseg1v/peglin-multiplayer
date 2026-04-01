using System;
using System.Collections.Generic;
using Battle;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

/// <summary>
/// Syncs pegboard state from host to client using GUID-based tracking.
///
/// Three-phase matching:
/// 1. GUID match (subsequent syncs, best case)
/// 2. Position match within 1 unit (first sync, handles matching layouts)
/// 3. Reposition: grab any unmatched client peg and move it to host position
///    (handles RNG-divergent random layouts where positions differ)
///
/// After matching, deactivate extras on client that don't exist on host.
/// </summary>
public class PegboardStateApplier : IGameStateApplier<PegboardStateSnapshot>
{
    private readonly ManualLogSource _log;
    private readonly PegIdentifier _pegId;

    public PegboardStateApplier(ManualLogSource log, PegIdentifier pegId)
    {
        _log = log;
        _pegId = pegId;
    }

    public void Apply(PegboardStateSnapshot snapshot)
    {
        try
        {
            if (snapshot.Pegs == null || snapshot.Pegs.Count == 0)
            {
                _log.LogInfo("[PegboardApplier] No pegs in snapshot.");
                return;
            }

            var bc = UnityEngine.Object.FindObjectOfType<Battle.BattleController>();
            var pm = bc?.pegManager;
            if (pm == null || pm.allPegs == null)
            {
                _log.LogInfo($"[PegboardApplier] No PegManager in scene. Snapshot has {snapshot.TotalPegCount} pegs.");
                return;
            }

            var clientPegs = pm.allPegs;
            var bombsField = HarmonyLib.AccessTools.Field(typeof(Battle.PegManager), "_bombs");
            var clientBombs = bombsField?.GetValue(pm) as System.Collections.Generic.List<Bomb>;

            int guidMatched = 0, posMatched = 0, repositioned = 0, typeChanged = 0,
                destroyed = 0, reactivated = 0, missed = 0;
            var matchedPegs = new HashSet<Peg>();

            // Collect all unmatched host peg entries for the reposition phase
            var unmatchedEntries = new List<PegEntry>();

            // ===== PHASE 1 & 2: GUID match, then position match =====
            foreach (var entry in snapshot.Pegs)
            {
                Peg peg = null;

                // Phase 1: GUID match
                if (!string.IsNullOrEmpty(entry.Guid))
                {
                    peg = _pegId.Find(entry.Guid);
                    if (peg != null)
                        guidMatched++;
                }

                // Phase 2: closest position within 1 unit
                if (peg == null)
                {
                    peg = FindClosestUnmatched(entry, clientPegs, clientBombs, matchedPegs, 1f);
                    if (peg != null)
                        posMatched++;
                }

                if (peg != null)
                {
                    matchedPegs.Add(peg);
                    // Always snap to exact host position (client RNG diverges on random layouts)
                    peg.transform.position = new Vector3(entry.PosX, entry.PosY, peg.transform.position.z);
                    ApplyPegState(peg, entry, ref typeChanged, ref destroyed, ref reactivated);
                }
                else
                {
                    unmatchedEntries.Add(entry);
                }
            }

            // ===== PHASE 3: Reposition unmatched client pegs to host positions =====
            // For host pegs that had no close match, grab any leftover client peg and
            // move it to the host position. This handles RNG-divergent random layouts
            // where client generated pegs at completely different positions.
            if (unmatchedEntries.Count > 0)
            {
                var availablePegs = new List<Peg>();
                foreach (var p in clientPegs)
                {
                    if (p != null && !matchedPegs.Contains(p))
                        availablePegs.Add(p);
                }
                if (clientBombs != null)
                {
                    foreach (var b in clientBombs)
                    {
                        if (b != null && !matchedPegs.Contains(b))
                            availablePegs.Add(b);
                    }
                }

                foreach (var entry in unmatchedEntries)
                {
                    if (availablePegs.Count == 0)
                    {
                        missed++;
                        continue;
                    }

                    // Take the last peg from the available pool (O(1) removal)
                    var peg = availablePegs[availablePegs.Count - 1];
                    availablePegs.RemoveAt(availablePegs.Count - 1);

                    // Reposition to host position
                    peg.transform.position = new Vector3(entry.PosX, entry.PosY, peg.transform.position.z);

                    matchedPegs.Add(peg);
                    repositioned++;
                    ApplyPegState(peg, entry, ref typeChanged, ref destroyed, ref reactivated);
                }
            }

            // ===== CLEANUP: Deactivate extra client pegs not in host snapshot =====
            int extrasRemoved = 0;
            foreach (var peg in clientPegs)
            {
                if (peg != null && peg.gameObject.activeSelf && !matchedPegs.Contains(peg))
                {
                    peg.gameObject.SetActive(false);
                    extrasRemoved++;
                }
            }
            if (clientBombs != null)
            {
                foreach (var bomb in clientBombs)
                {
                    if (bomb != null && bomb.gameObject.activeSelf && !matchedPegs.Contains(bomb))
                    {
                        bomb.gameObject.SetActive(false);
                        extrasRemoved++;
                    }
                }
            }

            int totalClient = clientPegs.Count + (clientBombs?.Count ?? 0);
            _log.LogInfo($"[PegboardApplier] GUIDMatched={guidMatched}, PosMatched={posMatched}, " +
                $"Repositioned={repositioned}, TypeChanged={typeChanged}, Destroyed={destroyed}, " +
                $"Reactivated={reactivated}, Missed={missed}, ExtrasRemoved={extrasRemoved} " +
                $"(host={snapshot.TotalPegCount}, client={totalClient}, " +
                $"crit={snapshot.CritPegCount}, bomb={snapshot.BombPegCount}, reset={snapshot.ResetPegCount}, " +
                $"registry={_pegId.Count})");

            LogActualPegState(clientPegs, clientBombs);
        }
        catch (Exception ex)
        {
            _log.LogError($"[PegboardApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Find the closest unmatched peg within the given distance threshold.
    /// </summary>
    private Peg FindClosestUnmatched(PegEntry entry, List<Peg> clientPegs,
        System.Collections.Generic.List<Bomb> clientBombs, HashSet<Peg> matched, float maxDist)
    {
        Peg closest = null;
        float closestDist = maxDist * maxDist; // compare squared distances

        foreach (var p in clientPegs)
        {
            if (p == null || matched.Contains(p)) continue;
            float dx = p.transform.position.x - entry.PosX;
            float dy = p.transform.position.y - entry.PosY;
            float dist = dx * dx + dy * dy;
            if (dist < closestDist) { closestDist = dist; closest = p; }
        }
        if (clientBombs != null)
        {
            foreach (var b in clientBombs)
            {
                if (b == null || matched.Contains(b)) continue;
                float dx = b.transform.position.x - entry.PosX;
                float dy = b.transform.position.y - entry.PosY;
                float dist = dx * dx + dy * dy;
                if (dist < closestDist) { closestDist = dist; closest = b; }
            }
        }
        return closest;
    }

    /// <summary>
    /// Apply host state (type, cleared, destroyed, slime, coins, bomb fuse) to a matched client peg.
    /// </summary>
    private void ApplyPegState(Peg peg, PegEntry entry,
        ref int typeChanged, ref int destroyed, ref int reactivated)
    {
        // Register with host GUID
        if (!string.IsNullOrEmpty(entry.Guid))
            _pegId.Register(peg, entry.Guid);

        bool clientPopped = false;
        try { clientPopped = peg.IsDisabled(); } catch { }

        // Handle cleared/popped pegs
        if (entry.IsCleared && !clientPopped)
        {
            try
            {
                peg.PegActivated(playAudio: false, forcePop: true);
                peg.RemoveIfCleared();
            }
            catch { }
        }

        // Handle destroyed pegs
        if (entry.IsDestroyed)
        {
            if (peg.gameObject.activeSelf && peg.pegType != Peg.PegType.DESTROYED)
            {
                try { peg.DestroyPeg(peg.pegType); }
                catch { peg.gameObject.SetActive(false); }
                destroyed++;
            }
            return;
        }

        // Reactivate if host says peg is alive but client has it disabled/popped
        if (!entry.IsCleared && !entry.IsDestroyed)
        {
            var clearedField = HarmonyLib.AccessTools.Field(typeof(Peg), "_cleared");

            if (!peg.gameObject.activeSelf || peg.pegType == Peg.PegType.DESTROYED || clientPopped)
            {
                clearedField?.SetValue(peg, entry.WasPreviouslyCleared);
                peg.gameObject.SetActive(true);
                try { peg.Reset(false); } catch { }
                reactivated++;
            }
            else
            {
                bool clientCleared = (bool)(clearedField?.GetValue(peg) ?? false);
                if (clientCleared != entry.WasPreviouslyCleared)
                {
                    clearedField?.SetValue(peg, entry.WasPreviouslyCleared);
                    try { peg.Reset(false); } catch { }
                }
            }
        }

        // Force peg type to match host
        var targetType = (Peg.PegType)entry.PegType;
        if (peg.pegType != targetType)
        {
            try
            {
                if (peg.SupportsPegType(targetType))
                {
                    var result = peg.ConvertPegToType(targetType);
                    if (targetType == Peg.PegType.BOMB && result != null && result != peg.gameObject)
                    {
                        var newBomb = result.GetComponent<Peg>();
                        if (newBomb != null && !string.IsNullOrEmpty(entry.Guid))
                            _pegId.Register(newBomb, entry.Guid);
                    }
                }
                else
                {
                    peg.pegType = targetType;
                }
            }
            catch
            {
                peg.pegType = targetType;
            }
            typeChanged++;
        }

        // Apply slime type
        var targetSlime = (Peg.SlimeType)entry.SlimeType;
        if (peg.slimeType != targetSlime)
        {
            try
            {
                if (targetSlime == Peg.SlimeType.None)
                    peg.RemoveSlime(true);
                else
                    peg.ApplySlimeToPeg(targetSlime);
            }
            catch { }
        }

        // Apply gold coins
        if (entry.CoinCount > 0)
        {
            int currentCoins = peg.NumCoins();
            for (int c = currentCoins; c < entry.CoinCount; c++)
            {
                try { peg.AddCoin(false); } catch { }
            }
        }

        // Sync bomb hit count
        if (entry.IsBomb && peg is Bomb bomb)
        {
            if (bomb.HitCount != entry.HitCount)
            {
                bomb.HitCount = entry.HitCount;
                try
                {
                    var animator = bomb.GetComponent<UnityEngine.Animator>();
                    animator?.SetInteger("NumHits", entry.HitCount);
                }
                catch { }
            }
        }
    }

    private void LogActualPegState(List<Peg> pegs, System.Collections.Generic.List<Bomb> bombs)
    {
        int active = 0, crits = 0, bombCount = 0, resets = 0, regular = 0;
        foreach (var peg in pegs)
        {
            if (peg == null || !peg.gameObject.activeSelf || peg.pegType == Peg.PegType.DESTROYED) continue;
            bool disabled = false;
            try { disabled = peg.IsDisabled(); } catch { }
            if (disabled) continue;
            active++;
            var pt = (int)peg.pegType;
            if ((pt & 0x2) != 0) crits++;
            else if ((pt & 0x8) != 0) resets++;
            else if ((pt & 0x1) != 0) regular++;
        }
        if (bombs != null)
        {
            foreach (var bomb in bombs)
            {
                if (bomb == null || !bomb.gameObject.activeSelf || bomb.pegType == Peg.PegType.DESTROYED) continue;
                bool disabled = false;
                try { disabled = bomb.IsDisabled(); } catch { }
                if (disabled) continue;
                active++;
                bombCount++;
            }
        }
        _log.LogInfo($"[PegboardApplier] CLIENT ACTUAL: {active} active pegs " +
            $"(regular={regular}, crit={crits}, bomb={bombCount}, reset={resets})");
    }
}
