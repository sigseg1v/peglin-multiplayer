using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Multipeglin.Events;
using Multipeglin.GameState.Providers;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;

namespace Multipeglin.GameState;

public class GameStateSyncService : IGameStateSyncService
{
    private readonly ManualLogSource _log;
    private readonly IGameEventRegistry _registry;
    private readonly IMultiplayerMode _mode;
    private readonly MapStateProvider _mapProvider;
    private readonly PlayerStateProvider _playerProvider;
    private readonly EnemyStateProvider _enemyProvider;
    private readonly PegboardStateProvider _pegboardProvider;
    private readonly DeckStateProvider _deckProvider;
    private readonly RelicStateProvider _relicProvider;
    private readonly CoopStateManager _coopStateManager;
    private readonly TextScenarioStateProvider _textScenarioProvider;
    private bool _mirrorEventDispatched;

    // Last-logged coop composition signature. We only log per-slot player /
    // deck lines when something actually changes — at 1-2s heartbeat cadence
    // with 4 players this was producing ~9 LogInfo calls every tick plus a
    // string.Join over the entire complete deck per slot per tick.
    private string _lastCoopLogSig;

    public GameStateSyncService(
        ManualLogSource log,
        IGameEventRegistry registry,
        IMultiplayerMode mode,
        EnemyIdentifier enemyId,
        PegIdentifier pegId,
        OrbIdentifier orbId,
        CoopStateManager coopStateManager = null)
    {
        _log = log;
        _registry = registry;
        _mode = mode;
        _mapProvider = new MapStateProvider(log);
        _playerProvider = new PlayerStateProvider(log);
        _enemyProvider = new EnemyStateProvider(log, enemyId);
        _pegboardProvider = new PegboardStateProvider(log, pegId);
        _deckProvider = new DeckStateProvider(log, orbId);
        _relicProvider = new RelicStateProvider(log);
        _textScenarioProvider = new TextScenarioStateProvider(log);
        _coopStateManager = coopStateManager;
    }

    public void SyncAll(string trigger = null)
    {
        if (!_mode.IsHosting)
        {
            return;
        }

        var tag = string.IsNullOrEmpty(trigger) ? string.Empty : $"[{trigger}] ";

        try
        {
            var activeSlot = _coopStateManager?.ActivePlayerSlot ?? 0;

            var snapshot = new FullGameStateSnapshot
            {
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Map = _mapProvider.Capture(),
                Player = _playerProvider.Capture(),
                Deck = _deckProvider.Capture(),
                Relics = _relicProvider.Capture(),
                Enemies = _enemyProvider.Capture(),
                Pegboard = _pegboardProvider.Capture(),
                TextScenario = _textScenarioProvider.Capture(),
            };

            // Tag per-player snapshots with the active slot so the client knows
            // whose data this is and can avoid applying another player's state.
            if (snapshot.Player != null)
            {
                snapshot.Player.ActiveSlotIndex = activeSlot;
            }

            if (snapshot.Deck != null)
            {
                snapshot.Deck.ActiveSlotIndex = activeSlot;
            }

            if (snapshot.Relics != null)
            {
                snapshot.Relics.ActiveSlotIndex = activeSlot;
            }

            // Add co-op multi-player data if available
            if (_coopStateManager != null && _coopStateManager.TotalPlayerCount > 0)
            {
                snapshot.ActivePlayerSlot = _coopStateManager.ActivePlayerSlot;
                snapshot.TotalPlayerCount = _coopStateManager.TotalPlayerCount;
                snapshot.PlayerSummaries = new List<CoopPlayerSummary>();

                snapshot.AllDecks = new Dictionary<int, Snapshots.DeckStateSnapshot>();
                snapshot.AllRelics = new Dictionary<int, Snapshots.RelicStateSnapshot>();

                foreach (var kvp in _coopStateManager.PlayerStates)
                {
                    var ps = kvp.Value;
                    // For the active player, use live singleton data
                    var isActive = kvp.Key == _coopStateManager.ActivePlayerSlot;
                    var summary = new CoopPlayerSummary
                    {
                        SlotIndex = ps.SlotIndex,
                        PlayerName = ps.PlayerName,
                        ChosenClass = ps.ChosenClass,
                        CurrentHealth = isActive ? (snapshot.Player?.CurrentHealth ?? ps.CurrentHealth) : ps.CurrentHealth,
                        MaxHealth = isActive ? (snapshot.Player?.MaxHealth ?? ps.MaxHealth) : ps.MaxHealth,
                        Gold = isActive ? (snapshot.Player?.Gold ?? ps.Gold) : ps.Gold,
                        HasShotThisRound = ps.HasShotThisRound,
                    };

                    // Per-player status effects: active player from singletons, others from CoopPlayerState
                    if (isActive && snapshot.Player?.StatusEffects != null)
                    {
                        summary.StatusEffects = snapshot.Player.StatusEffects;
                    }
                    else if (ps.StatusEffects != null)
                    {
                        summary.StatusEffects = ps.StatusEffects.ConvertAll(e => new Snapshots.StatusEffectEntry
                        {
                            EffectType = e.EffectType,
                            EffectName = ((Battle.StatusEffects.StatusEffectType)e.EffectType).ToString(),
                            Intensity = e.Intensity,
                        });
                    }

                    snapshot.PlayerSummaries.Add(summary);

                    // Per-player deck: active player from singletons, others from CoopPlayerState
                    if (isActive)
                    {
                        snapshot.AllDecks[kvp.Key] = snapshot.Deck;
                    }
                    else
                    {
                        snapshot.AllDecks[kvp.Key] = new Snapshots.DeckStateSnapshot
                        {
                            ActiveSlotIndex = kvp.Key,
                            DeckSize = ps.CompleteDeck?.Count ?? 0,
                            CompleteDeck = ps.CompleteDeck?.Select(o => new Snapshots.OrbEntry { Name = o.PrefabName, Guid = o.Guid, Level = o.Level }).ToList()
                                ?? new List<Snapshots.OrbEntry>(),
                            BattleDeck = ps.BattleDeck?.Select(o => new Snapshots.OrbEntry { Name = o.PrefabName, Guid = o.Guid, Level = o.Level }).ToList()
                                ?? new List<Snapshots.OrbEntry>(),
                            ShuffledOrder = ps.ShuffledOrder ?? new List<string>(),
                            // Non-active players have no drawn orb — CurrentOrb is only
                            // meaningful for the active player (captured live by DeckStateProvider).
                            CurrentOrb = null,
                            CurrentOrbLevel = 0,
                        };
                    }

                    // Per-player relics. Active player uses the live singleton capture
                    // (snapshot.Relics, which has accurate countdowns); inactive players
                    // are reconstructed from CoopPlayerState. Without this, every client
                    // applies the active player's relics regardless of slot, so coop
                    // players see each other's Pocket Sand, Slimy Salve countdowns, etc.
                    if (isActive)
                    {
                        snapshot.AllRelics[kvp.Key] = snapshot.Relics;
                    }
                    else
                    {
                        var relicSnap = new Snapshots.RelicStateSnapshot
                        {
                            ActiveSlotIndex = kvp.Key,
                            TotalRelicCount = ps.OwnedRelics?.Count ?? 0,
                        };
                        if (ps.OwnedRelics != null)
                        {
                            foreach (var sr in ps.OwnedRelics)
                            {
                                int countdown = 0, ups = 0, upb = 0, upr = 0;
                                ps.RelicCountdowns?.TryGetValue(sr.Effect, out countdown);
                                ps.RelicUsesPerShot?.TryGetValue(sr.Effect, out ups);
                                ps.RelicUsesPerBattle?.TryGetValue(sr.Effect, out upb);
                                ps.RelicUsesPerRun?.TryGetValue(sr.Effect, out upr);
                                relicSnap.OwnedRelics.Add(new Snapshots.RelicEntry
                                {
                                    Effect = sr.Effect,
                                    EffectName = ((Relics.RelicEffect)sr.Effect).ToString(),
                                    LocKey = sr.LocKey ?? string.Empty,
                                    Rarity = sr.Rarity,
                                    RemainingCountdown = countdown,
                                    RemainingUsesPerShot = ups,
                                    RemainingUsesPerBattle = upb,
                                    RemainingUsesPerRun = upr,
                                    IsEnabled = true,
                                });
                            }
                        }

                        snapshot.AllRelics[kvp.Key] = relicSnap;
                    }
                }
            }

            // Per-slot diagnostics. At 1-2s heartbeat cadence with 4 players the
            // old version emitted ~9 LogInfo calls every tick (and a string.Join
            // over each player's full complete-deck). Gate on a content signature
            // so we only log when something actually changes.
            if (snapshot.PlayerSummaries != null && snapshot.PlayerSummaries.Count > 0)
            {
                var sig = BuildCoopLogSignature(snapshot);
                if (sig != _lastCoopLogSig)
                {
                    _lastCoopLogSig = sig;
                    foreach (var s in snapshot.PlayerSummaries)
                    {
                        _log.LogInfo($"{tag}Player slot={s.SlotIndex} name={s.PlayerName} class={s.ChosenClass} hp={s.CurrentHealth}/{s.MaxHealth} gold={s.Gold} isHost={s.SlotIndex == 0}");
                    }

                    if (snapshot.AllDecks != null)
                    {
                        foreach (var dk in snapshot.AllDecks)
                        {
                            var orbs = dk.Value?.CompleteDeck;
                            var names = orbs != null ? string.Join(", ", orbs.Select(o => o.Name)) : "NULL";
                            _log.LogInfo($"{tag}AllDecks[{dk.Key}]: {orbs?.Count ?? 0} orbs [{names}] shuffled={dk.Value?.ShuffledOrder?.Count ?? 0} active={dk.Key == snapshot.ActivePlayerSlot}");
                        }
                    }
                }
            }

            // Mirror event detection — clients now handle TextScenario dialogue natively,
            // so we no longer dispatch MirrorEventStartEvent. The client sees the same
            // dialogue UI as the host and makes independent choices.
            if (snapshot.TextScenario?.IsMirrorEvent == true && !_mirrorEventDispatched)
            {
                _mirrorEventDispatched = true;
                _log.LogInfo($"{tag}Mirror event detected — clients handle natively via AllowTextScenarioLogic");
            }
            else if (snapshot.TextScenario?.IsMirrorEvent != true)
            {
                _mirrorEventDispatched = false;
            }

            _registry.Dispatch(snapshot);

            // Skip the per-tick summary line for heartbeat firings (one every 1-2s
            // per player × N players adds up). Keep it for explicit triggers so
            // we can still see scene loads / explicit sync events in the log.
            var isHeartbeat = trigger != null && trigger.StartsWith("HEARTBEAT");
            if (!isHeartbeat)
            {
                _log.LogInfo($"{tag}SyncAll: sent full state (map={snapshot.Map?.ActiveScene}, enemies={snapshot.Enemies?.Enemies?.Count ?? 0}, pegs={snapshot.Pegboard?.TotalPegCount ?? 0}, players={snapshot.TotalPlayerCount})");
                DiagnosticLogger.DumpBattleState("HOST_SyncAll");
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"{tag}SyncAll failed: {ex.Message}");
        }
    }

    private static string BuildCoopLogSignature(FullGameStateSnapshot snapshot)
    {
        // Cheap signature: slot|hp|gold for each player + decksize for each
        // slot + active slot. Avoids the string.Join over orb names on every
        // tick when nothing changed.
        var sb = new System.Text.StringBuilder(64);
        sb.Append(snapshot.ActivePlayerSlot);
        if (snapshot.PlayerSummaries != null)
        {
            foreach (var s in snapshot.PlayerSummaries)
            {
                sb.Append('|').Append(s.SlotIndex).Append(':')
                  .Append(s.CurrentHealth).Append('/').Append(s.MaxHealth)
                  .Append(',').Append(s.Gold);
            }
        }

        if (snapshot.AllDecks != null)
        {
            foreach (var dk in snapshot.AllDecks)
            {
                sb.Append('#').Append(dk.Key).Append(':')
                  .Append(dk.Value?.CompleteDeck?.Count ?? 0).Append('/')
                  .Append(dk.Value?.ShuffledOrder?.Count ?? 0);
            }
        }

        return sb.ToString();
    }

    public void SyncMap()
    {
        if (!_mode.IsHosting)
        {
            return;
        }

        var state = _mapProvider.Capture();
        if (state != null)
        {
            _registry.Dispatch(state);
        }

        _log.LogInfo($"SyncMap: scene={state?.ActiveScene}, floor={state?.TotalFloorCount}");
    }

    public void SyncPegboard()
    {
        if (!_mode.IsHosting)
        {
            return;
        }

        var state = _pegboardProvider.Capture();
        if (state != null)
        {
            _registry.Dispatch(state);
        }

        _log.LogInfo($"SyncPegboard: {state?.TotalPegCount} pegs ({state?.CritPegCount} crit, {state?.BombPegCount} bomb, {state?.ResetPegCount} reset)");
    }

    public void SyncEnemies()
    {
        if (!_mode.IsHosting)
        {
            return;
        }

        var state = _enemyProvider.Capture();
        if (state != null)
        {
            _registry.Dispatch(state);
        }

        _log.LogInfo($"SyncEnemies: {state?.Enemies?.Count ?? 0} enemies, battleState={state?.BattleStateName}");
    }

    public void SyncPlayer()
    {
        if (!_mode.IsHosting)
        {
            return;
        }

        var state = _playerProvider.Capture();
        if (state != null)
        {
            _registry.Dispatch(state);
        }

        _log.LogInfo($"SyncPlayer: hp={state?.CurrentHealth}/{state?.MaxHealth}, gold={state?.Gold}, effects={state?.StatusEffects?.Count ?? 0}");
    }

    public void SyncDeck()
    {
        if (!_mode.IsHosting)
        {
            return;
        }

        var state = _deckProvider.Capture();
        if (state != null)
        {
            _registry.Dispatch(state);
        }

        _log.LogInfo($"SyncDeck: {state?.DeckSize} orbs in complete deck, {state?.BattleDeck?.Count ?? 0} in battle deck");
    }

    public void SyncRelics()
    {
        if (!_mode.IsHosting)
        {
            return;
        }

        var state = _relicProvider.Capture();
        if (state != null)
        {
            _registry.Dispatch(state);
        }

        _log.LogInfo($"SyncRelics: {state?.TotalRelicCount} relics");
    }
}
