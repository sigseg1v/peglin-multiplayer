using BepInEx.Logging;
using Currency;
using Multipeglin.Events.Network.Currency;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;

namespace Multipeglin.Events.Subscriptions;

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

    private bool IsCoop =>
        _coopStateManager != null && _coopStateManager.TotalPlayerCount > 1;

    private void OnGoldAdded(int originalAmount, int currencyChange, bool silent)
    {
        if (!IsHosting) return;

        // In coop, distribute gold earned (peg hits, battle rewards) to all
        // inactive players. The active player already receives it via the
        // CurrencyManager singleton. Gold spending (shops) goes through
        // OnGoldRemoved, which is NOT distributed — each player spends their own.
        if (IsCoop && currencyChange > 0)
            _coopStateManager.DistributeGoldToInactivePlayers(currencyChange);

        // In coop, each player has their own gold. The host's CurrencyManager
        // reflects only whichever slot is active on the host — broadcasting its
        // changes to the client would overwrite the client's own per-slot gold.
        // Gold is synced per-slot via the PlayerState heartbeat, and client
        // shop spending flows through ShopPurchaseEvent. Skip the broadcast.
        if (IsCoop) return;

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

        // In coop, skip broadcasting host spending — see OnGoldAdded for rationale.
        if (IsCoop) return;

        _registry.Dispatch(new GoldChangedEvent
        {
            PreviousAmount = originalAmount,
            NewAmount = originalAmount - currencyChange,
            Delta = -currencyChange,
            IsGain = false
        });
    }
}
