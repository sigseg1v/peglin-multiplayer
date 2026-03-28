using System;
using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class FullGameStateClientHandler : IClientHandler<FullGameStateSnapshot>
{
    public void Handle(FullGameStateSnapshot e)
    {
        var log = MultiplayerPlugin.Logger;
        try
        {
            log?.LogInfo("=== FULL GAME STATE RECEIVED ===");

            if (e.Map != null)
                log?.LogInfo($"  Map: scene={e.Map.ActiveScene}, floor={e.Map.TotalFloorCount}, class={e.Map.ChosenClassName}, seed={e.Map.CurrentSeed}");

            if (e.Player != null)
                log?.LogInfo($"  Player: hp={e.Player.CurrentHealth}/{e.Player.MaxHealth}, gold={e.Player.Gold}, effects={e.Player.StatusEffects?.Count ?? 0}, speedup={e.Player.IsSpedUp}");

            if (e.Deck != null)
                log?.LogInfo($"  Deck: {e.Deck.DeckSize} orbs total, {e.Deck.BattleDeck?.Count ?? 0} in battle deck");

            if (e.Relics != null)
                log?.LogInfo($"  Relics: {e.Relics.TotalRelicCount} owned");

            if (e.Enemies != null)
            {
                log?.LogInfo($"  Battle: state={e.Enemies.BattleStateName}, {e.Enemies.Enemies?.Count ?? 0} enemies");
                foreach (var enemy in e.Enemies?.Enemies ?? new System.Collections.Generic.List<EnemyEntry>())
                    log?.LogInfo($"    Enemy: {enemy.LocKey} hp={enemy.CurrentHealth}/{enemy.MaxHealth} pos=({enemy.PosX:F1},{enemy.PosY:F1})");
            }

            if (e.Pegboard != null)
                log?.LogInfo($"  Pegboard: {e.Pegboard.TotalPegCount} pegs ({e.Pegboard.CritPegCount} crit, {e.Pegboard.BombPegCount} bomb, {e.Pegboard.ResetPegCount} reset)");

            log?.LogInfo("================================");
        }
        catch (Exception ex)
        {
            log?.LogError($"FullGameStateClientHandler: {ex.Message}");
        }
    }
}
