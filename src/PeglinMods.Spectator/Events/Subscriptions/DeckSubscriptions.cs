using BepInEx.Logging;
using PeglinMods.Spectator.Events.Network.Deck;
using PeglinMods.Spectator.Utility;
using UnityEngine;

namespace PeglinMods.Spectator.Events.Subscriptions;

public class DeckSubscriptions
{
    private readonly IGameEventRegistry _registry;
    private readonly OrbIdentifier _orbIdentifier;
    private readonly ManualLogSource _log;

    private DeckManager.BallDrawn _onBallDrawn;
    private DeckManager.BallDrawn _onBallUsed;
    private DeckManager.BallUpgraded _onBallUpgraded;
    private DeckManager.Shuffled _onDeckShuffled;

    public DeckSubscriptions(IGameEventRegistry registry, OrbIdentifier orbIdentifier, ManualLogSource log)
    {
        _registry = registry;
        _orbIdentifier = orbIdentifier;
        _log = log;
    }

    public void Subscribe()
    {
        _onBallDrawn = (GameObject ball) =>
        {
            _registry.Dispatch(new BallDrawnEvent
            {
                OrbName = _orbIdentifier.GetId(ball),
                Level = _orbIdentifier.GetLevel(ball)
            });
        };
        DeckManager.onBallDrawn += _onBallDrawn;

        _onBallUsed = (GameObject ball) =>
        {
            _registry.Dispatch(new BallUsedEvent
            {
                OrbName = _orbIdentifier.GetId(ball)
            });
        };
        DeckManager.onBallUsed += _onBallUsed;

        _onBallUpgraded = (GameObject previous, GameObject post) =>
        {
            _registry.Dispatch(new BallUpgradedEvent
            {
                PreviousOrbName = _orbIdentifier.GetId(previous),
                NewOrbName = _orbIdentifier.GetId(post),
                NewLevel = _orbIdentifier.GetLevel(post)
            });
        };
        DeckManager.onBallUpgraded += _onBallUpgraded;

        _onDeckShuffled = (int deckSize) =>
        {
            _registry.Dispatch(new DeckShuffledEvent { DeckSize = deckSize });
        };
        DeckManager.onDeckShuffled += _onDeckShuffled;

        _log.LogInfo("DeckSubscriptions registered");
    }

    public void Unsubscribe()
    {
        DeckManager.onBallDrawn -= _onBallDrawn;
        DeckManager.onBallUsed -= _onBallUsed;
        DeckManager.onBallUpgraded -= _onBallUpgraded;
        DeckManager.onDeckShuffled -= _onDeckShuffled;
    }
}
