using System;
using System.Linq;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Appliers;

public class PegboardStateApplier : IGameStateApplier<PegboardStateSnapshot>
{
    private readonly ManualLogSource _log;

    public PegboardStateApplier(ManualLogSource log) => _log = log;

    public void Apply(PegboardStateSnapshot snapshot)
    {
        try
        {
            var livePegs = UnityEngine.Object.FindObjectsOfType<Peg>();
            if (livePegs == null || livePegs.Length == 0)
            {
                _log.LogInfo($"[PegboardApplier] No pegs in scene. Snapshot has {snapshot.TotalPegCount} pegs.");
                return;
            }

            int updated = 0;
            int destroyed = 0;

            foreach (var entry in snapshot.Pegs)
            {
                var closestPeg = FindClosestPeg(livePegs, entry.PosX, entry.PosY);
                if (closestPeg == null)
                    continue;

                if (entry.IsDestroyed && closestPeg.gameObject.activeSelf)
                {
                    closestPeg.gameObject.SetActive(false);
                    destroyed++;
                    continue;
                }

                var snapshotPegType = (Peg.PegType)entry.PegType;
                if (closestPeg.pegType != snapshotPegType)
                {
                    _log.LogInfo($"[PegboardApplier] Peg at ({entry.PosX:F1},{entry.PosY:F1}) type changed: {closestPeg.pegType} -> {snapshotPegType}");
                    closestPeg.pegType = snapshotPegType;
                    updated++;
                }
            }

            _log.LogInfo($"[PegboardApplier] Applied: {livePegs.Length} live pegs, {updated} type-updated, {destroyed} destroyed. Snapshot: {snapshot.TotalPegCount} total ({snapshot.CritPegCount} crit, {snapshot.BombPegCount} bomb, {snapshot.ResetPegCount} reset)");
        }
        catch (Exception ex)
        {
            _log.LogError($"[PegboardApplier] Apply failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static Peg FindClosestPeg(Peg[] livePegs, float posX, float posY)
    {
        Peg closest = null;
        float closestDist = float.MaxValue;

        foreach (var peg in livePegs)
        {
            if (peg == null) continue;
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

        // Only match if within reasonable distance (0.5 units)
        return closestDist < 0.25f ? closest : null;
    }
}
