using System;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.GameState.Appliers;

public class RelicStateApplier : IGameStateApplier<RelicStateSnapshot>
{
    private readonly ManualLogSource _log;

    public RelicStateApplier(ManualLogSource log) => _log = log;

    public void Apply(RelicStateSnapshot snapshot)
    {
        try
        {
            // Modifying RelicManager at runtime is risky - relic effects hook into many
            // game systems. Log the received state for now.
            _log.LogInfo($"[RelicApplier] Relics sync: {snapshot.TotalRelicCount} relics owned");

            if (snapshot.OwnedRelics != null && snapshot.OwnedRelics.Count > 0)
            {
                foreach (var relic in snapshot.OwnedRelics)
                {
                    _log.LogInfo($"  - {relic.EffectName} (loc={relic.LocKey}, rarity={relic.Rarity}, enabled={relic.IsEnabled}, countdown={relic.RemainingCountdown})");
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"[RelicApplier] Apply failed: {ex.Message}");
        }
    }
}
