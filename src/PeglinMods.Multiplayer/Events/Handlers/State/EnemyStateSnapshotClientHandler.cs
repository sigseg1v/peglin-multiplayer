using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class EnemyStateSnapshotClientHandler : IClientHandler<EnemyStateSnapshot>
{
    public void Handle(EnemyStateSnapshot e)
    {
        var log = MultiplayerPlugin.Logger;
        log?.LogInfo($"[StateSync] Enemies: battleState={e.BattleStateName}, count={e.Enemies?.Count ?? 0}");
        foreach (var enemy in e.Enemies ?? new System.Collections.Generic.List<EnemyEntry>())
        {
            var effects = string.Join(",", enemy.StatusEffects?.ConvertAll(s => $"{s.EffectName}x{s.Intensity}") ?? new System.Collections.Generic.List<string>());
            log?.LogInfo($"  [{enemy.SlotIndex}] {enemy.LocKey}: hp={enemy.CurrentHealth}/{enemy.MaxHealth} dmg={enemy.MeleeDamage}/{enemy.RangedDamage} charge={enemy.CurrentCharge}/{enemy.ChargeTime} effects=[{effects}]");
        }
    }
}
