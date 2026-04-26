namespace Multipeglin.Events.Network.Peg;

public class PegHitEvent
{
    public int PegType { get; set; }

    public float PosX { get; set; }

    public float PosY { get; set; }

    public string PegGuid { get; set; }

    // Visual counters captured at hit time so the client can update the
    // specific hit peg without waiting for the 1s heartbeat. -1 = unknown.
    public int HitCount { get; set; } = -1;

    public int CoinCount { get; set; } = -1;

    public int ShieldHitCount { get; set; } = -1;

    public int ShieldHitLimit { get; set; } = -1;
}
