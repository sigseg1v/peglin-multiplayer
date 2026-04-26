using System.Collections.Generic;

namespace Multipeglin.GameState.Snapshots;

/// <summary>
/// Point-in-time snapshot of every in-flight PachinkoBall on the host.
/// Dispatched ~20 Hz while balls exist. Client reconciles by GUID:
/// spawn missing visuals, update present ones, destroy removed ones.
/// </summary>
public class BallStateSnapshot
{
    public float Timestamp { get; set; }

    public List<BallEntry> Balls { get; set; } = new List<BallEntry>();
}

public class BallEntry
{
    public string Guid { get; set; }

    public float PosX { get; set; }

    public float PosY { get; set; }

    public float VelX { get; set; }

    public float VelY { get; set; }

    /// <summary>Orb prefab name (e.g. "DaggerOrb-Lvl1(Clone)") for sprite lookup.</summary>
    public string OrbName { get; set; }

    /// <summary>Local scale X to match host (for scaling effects like Big relic).</summary>
    public float ScaleX { get; set; } = 1f;

    public float ScaleY { get; set; } = 1f;

    /// <summary>True if this is the primary ball (the one the player fired, not a multiball spawn).</summary>
    public bool IsPrimary { get; set; }
}
