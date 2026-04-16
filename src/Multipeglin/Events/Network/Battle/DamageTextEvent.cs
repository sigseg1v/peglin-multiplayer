namespace Multipeglin.Events.Network.Battle;

public class DamageTextEvent
{
    public string Text { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float R { get; set; }
    public float G { get; set; }
    public float B { get; set; }
    public float A { get; set; }
    public float Scale { get; set; } = 1f;
}
