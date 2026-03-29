using System;
using BepInEx.Logging;
using HarmonyLib;
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

            // PegManager is not a MonoBehaviour - it's a plain class held by BattleController
            var bc = UnityEngine.Object.FindObjectOfType<Battle.BattleController>();
            if (bc == null) return snapshot;

            var pmField = AccessTools.Field(typeof(Battle.BattleController), "_pegManager")
                ?? AccessTools.Field(typeof(Battle.BattleController), "pegManager");

            if (pmField == null)
            {
                // Fallback: find all Peg components in scene
                var allPegs = UnityEngine.Object.FindObjectsOfType<Peg>(true);
                foreach (var peg in allPegs)
                {
                    snapshot.Pegs.Add(CreatePegEntry(peg));
                }
                snapshot.TotalPegCount = allPegs.Length;
                return snapshot;
            }

            var pm = pmField.GetValue(bc);
            if (pm == null) return snapshot;

            // Get allPegs list from PegManager
            System.Collections.IList pegList = null;
            var allPegsField = AccessTools.Field(pm.GetType(), "_allPegs");
            if (allPegsField != null)
                pegList = allPegsField.GetValue(pm) as System.Collections.IList;
            if (pegList == null)
            {
                var allPegsProp = AccessTools.Property(pm.GetType(), "allPegs");
                pegList = allPegsProp?.GetValue(pm) as System.Collections.IList;
            }

            if (pegList != null)
            {
                foreach (var pegObj in pegList)
                {
                    var peg = pegObj as Peg;
                    if (peg == null) continue;

                    var entry = CreatePegEntry(peg);
                    snapshot.Pegs.Add(entry);

                    // Count by type
                    var pt = (int)peg.pegType;
                    if ((pt & 0x2) != 0) snapshot.CritPegCount++;   // CRIT = 2
                    if ((pt & 0x4) != 0) snapshot.BombPegCount++;   // BOMB = 4
                    if ((pt & 0x8) != 0) snapshot.ResetPegCount++;  // RESET = 8
                }
                snapshot.TotalPegCount = pegList.Count;
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"PegboardStateProvider.Capture failed: {ex.Message}");
            return null;
        }
    }

    private static PegEntry CreatePegEntry(Peg peg)
    {
        return new PegEntry
        {
            PegType = (int)peg.pegType,
            PegTypeName = peg.pegType.ToString(),
            PosX = peg.transform.position.x,
            PosY = peg.transform.position.y,
            SlimeType = (int)peg.slimeType,
            IsDestroyed = !peg.gameObject.activeSelf || ((int)peg.pegType & 0x20) != 0,
        };
    }
}
