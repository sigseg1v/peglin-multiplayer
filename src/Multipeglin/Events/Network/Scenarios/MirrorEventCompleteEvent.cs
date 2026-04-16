namespace Multipeglin.Events.Network.Scenarios;

/// <summary>
/// Client → host: the client has completed their Mirror event choice.
/// Host applies the deck modification to the client's CoopPlayerState.
/// </summary>
public class MirrorEventCompleteEvent
{
    /// <summary>"remove_one" or "remove_all"</summary>
    public string Action { get; set; }

    /// <summary>Prefab name of the removed orb (for remove_one).</summary>
    public string RemovedOrbName { get; set; }

    /// <summary>GUID of the removed orb (for remove_one, used for precise matching).</summary>
    public string RemovedOrbGuid { get; set; }
}
