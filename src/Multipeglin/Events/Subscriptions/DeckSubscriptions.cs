using BepInEx.Logging;
using Multipeglin.Events.Network.Deck;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.Events.Subscriptions;

public sealed class DeckSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly OrbIdentifier _orbIdentifier;
    private readonly ManualLogSource _log;

    public DeckSubscriptions(IGameEventRegistry registry, OrbIdentifier orbIdentifier, ManualLogSource log)
    {
        _registry = registry;
        _orbIdentifier = orbIdentifier;
        _log = log;
    }

    private static bool IsHosting =>
        MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting;

    public void Subscribe()
    {
        DeckManager.onBallDrawn += OnBallDrawn;
        DeckManager.onBallUsed += OnBallUsed;
        DeckManager.onBallUpgraded += OnBallUpgraded;
        DeckManager.onDeckShuffled += OnDeckShuffled;
        _log.LogInfo("DeckSubscriptions registered");
    }

    public void Unsubscribe()
    {
        DeckManager.onBallDrawn -= OnBallDrawn;
        DeckManager.onBallUsed -= OnBallUsed;
        DeckManager.onBallUpgraded -= OnBallUpgraded;
        DeckManager.onDeckShuffled -= OnDeckShuffled;
    }

    private void OnBallDrawn(GameObject ball)
    {
        if (!IsHosting)
        {
            return;
        }

        _registry.Dispatch(new BallDrawnEvent
        {
            OrbName = _orbIdentifier.GetId(ball),
            Level = _orbIdentifier.GetLevel(ball)
        });
    }

    private void OnBallUsed(GameObject ball)
    {
        if (!IsHosting)
        {
            return;
        }

        _registry.Dispatch(new BallUsedEvent
        {
            OrbName = _orbIdentifier.GetId(ball)
        });
    }

    private void OnBallUpgraded(GameObject previous, GameObject post)
    {
        if (!IsHosting)
        {
            return;
        }

        _registry.Dispatch(new BallUpgradedEvent
        {
            PreviousOrbName = _orbIdentifier.GetId(previous),
            NewOrbName = _orbIdentifier.GetId(post),
            NewLevel = _orbIdentifier.GetLevel(post)
        });
    }

    private void OnDeckShuffled(int deckSize)
    {
        if (!IsHosting)
        {
            return;
        }

        _registry.Dispatch(new DeckShuffledEvent { DeckSize = deckSize });
    }
}
