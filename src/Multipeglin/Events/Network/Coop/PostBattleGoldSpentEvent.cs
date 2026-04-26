namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Client -> host: client spent gold on the native post-battle reward screen
/// (heal, max HP, orb upgrade, orb add). Sent immediately per-purchase so the
/// host's CoopPlayerState.Gold reflects the deduction before the next heartbeat
/// overwrites the client's local CurrencyManager.GoldAmount.
///
/// Also carries the client's post-purchase CurrentHealth/MaxHealth so host-side
/// CoopPlayerState matches heals and max-HP increases from the native reward UI.
/// Without this the heartbeat reverts the client's HP back to the pre-purchase
/// value until the next battle triggers SaveActivePlayerState.
/// </summary>
public class PostBattleGoldSpentEvent
{
    public int Amount { get; set; }

    public float CurrentHealth { get; set; }

    public float MaxHealth { get; set; }
}
