using System.Collections.Generic;

namespace PeglinMods.Multiplayer.GameState.Snapshots;

public class MapStateSnapshot
{
    public string CurrentSeed { get; set; }
    public int TotalFloorCount { get; set; }
    public int ChosenClass { get; set; }
    public string ChosenClassName { get; set; }
    public int CruciballLevel { get; set; }
    public string ActiveScene { get; set; }
    public int ChosenNextNodeIndex { get; set; }
    public bool HasReachedBoss { get; set; }

    /// <summary>
    /// Serialized UnityEngine.Random.State captured BEFORE map generation.
    /// Restoring this on the client ensures identical procedural content.
    /// Format: "s0,s1,s2,s3" (4 ints from the internal state fields).
    /// </summary>
    public string RandomStateJson { get; set; }
}
