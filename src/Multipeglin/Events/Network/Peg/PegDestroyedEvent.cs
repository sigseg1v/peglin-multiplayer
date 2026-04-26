namespace Multipeglin.Events.Network.Peg;

public class PegDestroyedEvent
{
    public int PegType { get; set; }

    public float PosX { get; set; }

    public float PosY { get; set; }

    public string PegGuid { get; set; }
}
