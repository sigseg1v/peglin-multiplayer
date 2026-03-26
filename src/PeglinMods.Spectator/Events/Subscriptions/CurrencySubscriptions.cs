using BepInEx.Logging;
using Currency;
using PeglinMods.Spectator.Events.Network.Currency;

namespace PeglinMods.Spectator.Events.Subscriptions;

public class CurrencySubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly ManualLogSource _log;

    private CurrencyManager.CurrencyEvent _onGoldAdded;
    private CurrencyManager.CurrencyEvent _onGoldRemoved;

    public CurrencySubscriptions(IGameEventRegistry registry, ManualLogSource log)
    {
        _registry = registry;
        _log = log;
    }

    public void Subscribe()
    {
        _onGoldAdded = (int originalAmount, int currencyChange, bool silent) =>
        {
            _registry.Dispatch(new GoldChangedEvent
            {
                PreviousAmount = originalAmount,
                NewAmount = originalAmount + currencyChange,
                Delta = currencyChange,
                IsGain = true
            });
        };
        CurrencyManager.OnGoldAdded += _onGoldAdded;

        _onGoldRemoved = (int originalAmount, int currencyChange, bool silent) =>
        {
            _registry.Dispatch(new GoldChangedEvent
            {
                PreviousAmount = originalAmount,
                NewAmount = originalAmount - currencyChange,
                Delta = -currencyChange,
                IsGain = false
            });
        };
        CurrencyManager.OnGoldRemoved += _onGoldRemoved;

        _log.LogInfo("CurrencySubscriptions registered");
    }

    public void Unsubscribe()
    {
        CurrencyManager.OnGoldAdded -= _onGoldAdded;
        CurrencyManager.OnGoldRemoved -= _onGoldRemoved;
    }
}
