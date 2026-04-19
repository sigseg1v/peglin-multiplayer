namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Client -> host: client spent gold on the native post-battle reward screen
/// (heal, max HP, orb upgrade, orb add). Sent immediately per-purchase so the
/// host's CoopPlayerState.Gold reflects the deduction before the next heartbeat
/// overwrites the client's local CurrencyManager.GoldAmount.
/// </summary>
public class PostBattleGoldSpentEvent
{
    public int Amount { get; set; }
}
