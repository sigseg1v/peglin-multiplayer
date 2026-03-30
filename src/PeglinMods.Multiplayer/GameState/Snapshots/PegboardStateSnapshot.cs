using System.Collections.Generic;

namespace PeglinMods.Multiplayer.GameState.Snapshots;

public class PegboardStateSnapshot
{
    public List<PegEntry> Pegs { get; set; } = new List<PegEntry>();
    public int TotalPegCount { get; set; }
    public int CritPegCount { get; set; }
    public int BombPegCount { get; set; }
    public int ResetPegCount { get; set; }
}

public class PegEntry
{
    public string Guid { get; set; }

    /// <summary>Index in PegManager.allPegs — used for GUID assignment on first sync.</summary>
    public int Index { get; set; }

    public int PegType { get; set; }
    public string PegTypeName { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public int SlimeType { get; set; }
    public bool IsDestroyed { get; set; }

    /// <summary>Number of gold coins on this peg (0 = no gold).</summary>
    public int CoinCount { get; set; }

    /// <summary>Hit count for bombs (0=untouched, 1=fuse lit, 2+=detonated).</summary>
    public int HitCount { get; set; }

    /// <summary>True if this peg is a bomb (from _bombs list, not _allPegs).</summary>
    public bool IsBomb { get; set; }
}
