namespace Multipeglin.Events.Network.Ball;

public class MultiballPositionEvent
{
    public string Guid { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float VelX { get; set; }
    public float VelY { get; set; }
    public float Timestamp { get; set; }
}
