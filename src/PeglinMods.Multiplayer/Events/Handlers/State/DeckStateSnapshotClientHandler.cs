using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class DeckStateSnapshotClientHandler : IClientHandler<DeckStateSnapshot>
{
    public void Handle(DeckStateSnapshot e)
    {
        var log = MultiplayerPlugin.Logger;
        log?.LogInfo($"[StateSync] Deck: {e.DeckSize} total orbs, {e.BattleDeck?.Count ?? 0} in battle deck");
        foreach (var orb in e.CompleteDeck ?? new System.Collections.Generic.List<OrbEntry>())
            log?.LogInfo($"  Orb: {orb.LocName} (lv{orb.Level}) dmg={orb.BaseDamage} crit={orb.CritDamage}");
    }
}
