using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class PlayerStateSnapshotClientHandler : IClientHandler<PlayerStateSnapshot>
{
    public void Handle(PlayerStateSnapshot e)
    {
        var effects = string.Join(", ", e.StatusEffects?.ConvertAll(s => $"{s.EffectName}x{s.Intensity}") ?? new System.Collections.Generic.List<string>());
        MultiplayerPlugin.Logger?.LogInfo($"[StateSync] Player: hp={e.CurrentHealth}/{e.MaxHealth}, gold={e.Gold}, speedup={e.IsSpedUp}, effects=[{effects}]");
    }
}
