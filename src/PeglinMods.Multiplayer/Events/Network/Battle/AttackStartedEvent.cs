namespace PeglinMods.Multiplayer.Events.Network.Battle;

public class AttackStartedEvent
{
    /// <summary>Animator trigger for the peglin attack animation (e.g. "attack").</summary>
    public string AnimTrigger { get; set; }

    /// <summary>GUID of the target enemy being attacked.</summary>
    public string TargetEnemyGuid { get; set; }
}
