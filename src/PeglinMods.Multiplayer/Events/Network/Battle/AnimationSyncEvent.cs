namespace PeglinMods.Multiplayer.Events.Network.Battle;

public class AnimationSyncEvent
{
    /// <summary>GUID of the entity (enemy or peg) playing the animation.</summary>
    public string EntityGuid { get; set; }

    /// <summary>"trigger" or "bool" or "integer"</summary>
    public string ParamType { get; set; }

    /// <summary>Animator parameter name.</summary>
    public string ParamName { get; set; }

    /// <summary>Value for bool/integer params.</summary>
    public int Value { get; set; }

    /// <summary>Position for entity lookup fallback.</summary>
    public float PosX { get; set; }
    public float PosY { get; set; }
}
