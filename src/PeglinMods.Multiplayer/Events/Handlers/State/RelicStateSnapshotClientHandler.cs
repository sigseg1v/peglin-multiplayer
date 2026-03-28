using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class RelicStateSnapshotClientHandler : IClientHandler<RelicStateSnapshot>
{
    public void Handle(RelicStateSnapshot e)
    {
        var log = MultiplayerPlugin.Logger;
        log?.LogInfo($"[StateSync] Relics: {e.TotalRelicCount} owned");
        foreach (var relic in e.OwnedRelics ?? new System.Collections.Generic.List<RelicEntry>())
            log?.LogInfo($"  Relic: {relic.EffectName} ({relic.LocKey}) rarity={relic.Rarity} countdown={relic.RemainingCountdown}");
    }
}
