using BepInEx.Logging;
using Currency;
using PeglinMods.Multiplayer.Events.Network.Currency;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;

namespace PeglinMods.Multiplayer.Events.Subscriptions;

public sealed class CurrencySubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;
    private readonly CoopStateManager _coopStateManager;

    public CurrencySubscriptions(IGameEventRegistry registry, ManualLogSource log,
        CoopStateManager coopStateManager = null)
    {
        _registry = registry;
        _log = log;
        _coopStateManager = coopStateManager;
    }

    private static bool IsHosting =>
        MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting;

    public void Subscribe()
    {
        CurrencyManager.OnGoldAdded += OnGoldAdded;
        CurrencyManager.OnGoldRemoved += OnGoldRemoved;
        _log.LogInfo("CurrencySubscriptions registered");
    }

    public void Unsubscribe()
    {
        CurrencyManager.OnGoldAdded -= OnGoldAdded;
        CurrencyManager.OnGoldRemoved -= OnGoldRemoved;
    }

    private void OnGoldAdded(int originalAmount, int currencyChange, bool silent)
    {
        if (!IsHosting) return;

        // In coop, distribute gold earned (peg hits, battle rewards) to all
        // inactive players. The active player already receives it via the
        // CurrencyManager singleton. Gold spending (shops) goes through
        // OnGoldRemoved, which is NOT distributed — each player spends their own.
        if (_coopStateManager != null && _coopStateManager.TotalPlayerCount > 1 && currencyChange > 0)
            _coopStateManager.DistributeGoldToInactivePlayers(currencyChange);

        _registry.Dispatch(new GoldChangedEvent
        {
            PreviousAmount = originalAmount,
            NewAmount = originalAmount + currencyChange,
            Delta = currencyChange,
            IsGain = true
        });
    }

    private void OnGoldRemoved(int originalAmount, int currencyChange, bool silent)
    {
        if (!IsHosting) return;
        _registry.Dispatch(new GoldChangedEvent
        {
            PreviousAmount = originalAmount,
            NewAmount = originalAmount - currencyChange,
            Delta = -currencyChange,
            IsGain = false
        });
    }
}
