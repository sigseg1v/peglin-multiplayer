namespace Multipeglin.Events.Subscriptions.Coop;

/// <summary>
/// Coop-specific deck operations performed on whatever player slot is currently
/// loaded into DeckManager / RelicManager / PlayerStatusEffectController. Each
/// method assumes the caller has already swapped CoopStateManager state into
/// the singletons before calling.
/// </summary>
public interface ICoopDeckManager
{
    /// <summary>
    /// Make sure the loaded slot's BattleDeck and ShuffledDeck are non-empty.
    /// Returns true on success. Behaviour matches the original CoopSubscriptions
    /// implementation: it skips re-shuffling when both decks are populated, and
    /// initializes from completeDeck via ShuffleCompleteDeck(true) when needed.
    /// </summary>
    bool EnsureBattleDeckPopulated(string context);

    /// <summary>
    /// Replicate BattleController.ApplyStartingBonuses for a non-host slot at
    /// battle init. Resets per-battle relic counters and runs
    /// PlayerStatusEffectController.ApplyStartingBonuses against the currently
    /// loaded slot's relics.
    /// </summary>
    void ApplyNonHostStartingBonuses(int slot);
}
