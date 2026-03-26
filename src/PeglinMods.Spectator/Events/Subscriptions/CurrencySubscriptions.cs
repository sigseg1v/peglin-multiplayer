using BepInEx.Logging;
using Currency;
using PeglinMods.Spectator.Events.Network.Currency;
using PeglinMods.Spectator.Spectator;

namespace PeglinMods.Spectator.Events.Subscriptions;

public sealed class CurrencySubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    public CurrencySubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    private static bool IsHosting =>
        SpectatorPlugin.Services?.TryResolve<ISpectatorMode>(out var mode) == true && mode.IsHosting;

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
