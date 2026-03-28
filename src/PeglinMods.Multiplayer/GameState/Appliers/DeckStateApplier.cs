using System;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.GameState.Appliers;

public class DeckStateApplier : IGameStateApplier<DeckStateSnapshot>
{
    private readonly ManualLogSource _log;

    public DeckStateApplier(ManualLogSource log) => _log = log;

    public void Apply(DeckStateSnapshot snapshot)
    {
        try
        {
            // Modifying the ScriptableObject-based deck at runtime is risky and can
            // desync internal state. Log the received data for now.
            _log.LogInfo($"[DeckApplier] Deck sync: {snapshot.DeckSize} orbs total, current orb: {snapshot.CurrentOrb ?? "none"} (level {snapshot.CurrentOrbLevel})");

            if (snapshot.CompleteDeck != null && snapshot.CompleteDeck.Count > 0)
            {
                _log.LogInfo($"[DeckApplier] Complete deck ({snapshot.CompleteDeck.Count} orbs):");
                foreach (var orb in snapshot.CompleteDeck)
                    _log.LogInfo($"  - {orb.LocName ?? orb.Name} lv{orb.Level} (dmg={orb.BaseDamage}, crit={orb.CritDamage})");
            }

            if (snapshot.BattleDeck != null && snapshot.BattleDeck.Count > 0)
            {
                _log.LogInfo($"[DeckApplier] Battle deck ({snapshot.BattleDeck.Count} orbs):");
                foreach (var orb in snapshot.BattleDeck)
                    _log.LogInfo($"  - {orb.LocName ?? orb.Name} lv{orb.Level}");
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"[DeckApplier] Apply failed: {ex.Message}");
        }
    }
}
