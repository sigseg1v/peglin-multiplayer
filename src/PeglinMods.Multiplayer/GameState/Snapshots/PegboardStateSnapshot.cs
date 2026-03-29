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
}
