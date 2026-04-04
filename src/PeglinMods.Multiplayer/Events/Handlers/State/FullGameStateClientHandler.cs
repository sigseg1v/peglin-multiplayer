using System;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.UI;

namespace PeglinMods.Multiplayer.Events.Handlers.State;

public sealed class FullGameStateClientHandler : IClientHandler<FullGameStateSnapshot>
{
    public void Handle(FullGameStateSnapshot e)
    {
        var log = MultiplayerPlugin.Logger;
        try
        {
            // Always update co-op player visuals data from every heartbeat
            if (e.PlayerSummaries != null)
                CoopPlayerVisuals.LatestPlayerSummaries = e.PlayerSummaries;
            CoopPlayerVisuals.LatestActiveSlot = e.ActivePlayerSlot;

            var mode = MultiplayerPlugin.Services?.Resolve<IMultiplayerMode>();
            if (mode?.ClientMode == ClientMode.Mirror)
            {
                // Apply state to the live game
                var applyService = MultiplayerPlugin.Services?.Resolve<GameStateApplyService>();
                if (applyService != null)
                {
                    applyService.ApplyAll(e);
                    return;
                }
            }

            // Diagnostics mode: log everything
            log?.LogInfo("=== FULL GAME STATE RECEIVED ===");
            if (e.Map != null)
                log?.LogInfo($"  Map: scene={e.Map.ActiveScene}, floor={e.Map.TotalFloorCount}, class={e.Map.ChosenClassName}, seed={e.Map.CurrentSeed}");
            if (e.Player != null)
                log?.LogInfo($"  Player: hp={e.Player.CurrentHealth}/{e.Player.MaxHealth}, gold={e.Player.Gold}, effects={e.Player.StatusEffects?.Count ?? 0}");
            if (e.Deck != null)
                log?.LogInfo($"  Deck: {e.Deck.DeckSize} orbs total");
            if (e.Relics != null)
                log?.LogInfo($"  Relics: {e.Relics.TotalRelicCount} owned");
            if (e.Enemies != null)
                log?.LogInfo($"  Battle: state={e.Enemies.BattleStateName}, {e.Enemies.Enemies?.Count ?? 0} enemies");
            if (e.Pegboard != null)
                log?.LogInfo($"  Pegboard: {e.Pegboard.TotalPegCount} pegs");
            if (e.PlayerSummaries != null)
                log?.LogInfo($"  CoopPlayers: {e.PlayerSummaries.Count} players, activeSlot={e.ActivePlayerSlot}");
            log?.LogInfo("================================");
        }
        catch (Exception ex)
        {
            log?.LogError($"FullGameStateClientHandler: {ex.Message}");
        }
    }
}
