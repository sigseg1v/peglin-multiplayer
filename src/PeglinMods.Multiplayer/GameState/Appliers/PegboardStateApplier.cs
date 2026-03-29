using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

/// <summary>
/// Aggressively syncs pegboard state from host to client.
/// Force-converts peg types to match host. Deactivates destroyed pegs.
/// Deactivates extra pegs not in host snapshot.
/// </summary>
public class PegboardStateApplier : IGameStateApplier<PegboardStateSnapshot>
{
    private readonly ManualLogSource _log;

    public PegboardStateApplier(ManualLogSource log) => _log = log;

    public void Apply(PegboardStateSnapshot snapshot)
    {
        try
        {
            if (snapshot.Pegs == null || snapshot.Pegs.Count == 0)
            {
                _log.LogInfo("[PegboardApplier] No pegs in snapshot.");
                return;
            }

            // Find ALL pegs including inactive ones (we might need to reactivate)
            var livePegs = UnityEngine.Object.FindObjectsOfType<Peg>(true);
            if (livePegs == null || livePegs.Length == 0)
            {
                _log.LogInfo($"[PegboardApplier] No pegs in scene. Snapshot has {snapshot.TotalPegCount} pegs.");
                return;
            }

            var matched = new HashSet<Peg>();
            int typeChanged = 0, destroyed = 0, reactivated = 0;

            // Pass 1: match host pegs to client pegs, update state
            foreach (var entry in snapshot.Pegs)
            {
                var peg = FindClosestPeg(livePegs, entry.PosX, entry.PosY, matched);
                if (peg == null) continue;
                matched.Add(peg);

                // Handle destroyed pegs
                if (entry.IsDestroyed)
                {
                    if (peg.gameObject.activeSelf)
                    {
                        peg.gameObject.SetActive(false);
                        destroyed++;
                    }
                    continue;
                }

                // Reactivate if host says it's alive but client has it deactivated
                if (!peg.gameObject.activeSelf)
                {
                    peg.gameObject.SetActive(true);
                    reactivated++;
                }

                // Force peg type to match host
                var targetType = (Peg.PegType)entry.PegType;
                if (peg.pegType != targetType)
                {
                    try
                    {
                        if (peg.SupportsPegType(targetType))
                            peg.ConvertPegToType(targetType);
                        else
                            peg.pegType = targetType; // Fallback: direct field set
                    }
                    catch
                    {
                        peg.pegType = targetType; // Fallback on exception
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
            }

            // Pass 2: deactivate client pegs not in host snapshot
            int extras = 0;
            foreach (var peg in livePegs)
            {
                if (peg != null && !matched.Contains(peg) && peg.gameObject.activeSelf)
                {
                    peg.gameObject.SetActive(false);
                    extras++;
                }
            }

            _log.LogInfo($"[PegboardApplier] Matched={matched.Count}, TypeChanged={typeChanged}, " +
                $"Destroyed={destroyed}, Reactivated={reactivated}, ExtrasRemoved={extras} " +
                $"(host={snapshot.TotalPegCount}, client={livePegs.Length}, " +
                $"crit={snapshot.CritPegCount}, bomb={snapshot.BombPegCount}, reset={snapshot.ResetPegCount})");
        }
        catch (Exception ex)
        {
            _log.LogError($"[PegboardApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Find the closest peg to the given position that hasn't already been matched.
    /// Uses 1.0 unit tolerance for position matching.
    /// </summary>
    private static Peg FindClosestPeg(Peg[] livePegs, float posX, float posY, HashSet<Peg> alreadyMatched)
    {
        Peg closest = null;
        float closestDist = float.MaxValue;

        foreach (var peg in livePegs)
        {
            if (peg == null || alreadyMatched.Contains(peg)) continue;
            var pos = peg.transform.position;
            float dx = pos.x - posX;
            float dy = pos.y - posY;
            float dist = dx * dx + dy * dy;
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = peg;
            }
        }

        // Accept within 1.0 units (squared = 1.0)
        return closestDist <= 1f ? closest : null;
    }
}
