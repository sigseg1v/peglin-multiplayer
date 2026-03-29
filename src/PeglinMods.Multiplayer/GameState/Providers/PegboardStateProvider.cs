using System;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState.Providers;

public class PegboardStateProvider : IGameStateProvider<PegboardStateSnapshot>
{
    private readonly ManualLogSource _log;

    public PegboardStateProvider(ManualLogSource log) => _log = log;

    public PegboardStateSnapshot Capture()
    {
        try
        {
            var snapshot = new PegboardStateSnapshot();

            // Find ALL pegs in the scene — includes RegularPeg, LongPeg, Bomb, BouncerPeg
            // (Bomb and BouncerPeg extend Peg but are tracked in separate lists by PegManager)
            var allPegs = UnityEngine.Object.FindObjectsOfType<Peg>(true);

            foreach (var peg in allPegs)
            {
                if (peg == null) continue;

                snapshot.Pegs.Add(new PegEntry
                {
                    PegType = (int)peg.pegType,
                    PegTypeName = peg.pegType.ToString(),
                    PosX = peg.transform.position.x,
                    PosY = peg.transform.position.y,
                    SlimeType = (int)peg.slimeType,
                    IsDestroyed = !peg.gameObject.activeSelf || ((int)peg.pegType & 0x20) != 0,
                });

                // Count by type
                var pt = (int)peg.pegType;
                if (peg.gameObject.activeSelf)
                {
                    if ((pt & 0x2) != 0) snapshot.CritPegCount++;
                    if ((pt & 0x4) != 0) snapshot.BombPegCount++;
                    if ((pt & 0x8) != 0) snapshot.ResetPegCount++;
                }
            }
            snapshot.TotalPegCount = allPegs.Length;

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"PegboardStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }
}
