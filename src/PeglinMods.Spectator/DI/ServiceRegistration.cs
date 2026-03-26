using BepInEx.Logging;
using PeglinMods.Spectator.Events;
using PeglinMods.Spectator.Events.Handlers;
using PeglinMods.Spectator.Events.Handlers.Ball;
using PeglinMods.Spectator.Events.Handlers.Battle;
using PeglinMods.Spectator.Events.Handlers.Currency;
using PeglinMods.Spectator.Events.Handlers.Deck;
using PeglinMods.Spectator.Events.Handlers.Enemy;
using PeglinMods.Spectator.Events.Handlers.Health;
using PeglinMods.Spectator.Events.Handlers.Map;
using PeglinMods.Spectator.Events.Handlers.Peg;
using PeglinMods.Spectator.Events.Handlers.Relic;
using PeglinMods.Spectator.Events.Handlers.StatusEffect;
using PeglinMods.Spectator.Events.Network;
using PeglinMods.Spectator.Events.Network.Ball;
using PeglinMods.Spectator.Events.Network.Battle;
using PeglinMods.Spectator.Events.Network.Currency;
using PeglinMods.Spectator.Events.Network.Deck;
using PeglinMods.Spectator.Events.Network.Enemy;
using PeglinMods.Spectator.Events.Network.Health;
using PeglinMods.Spectator.Events.Network.Map;
using PeglinMods.Spectator.Events.Network.Peg;
using PeglinMods.Spectator.Events.Network.Relic;
using PeglinMods.Spectator.Events.Network.StatusEffect;
using PeglinMods.Spectator.Events.Subscriptions;
using PeglinMods.Spectator.Network;
using PeglinMods.Spectator.Network.Protocol;
using PeglinMods.Spectator.Spectator;
using PeglinMods.Spectator.Utility;

namespace PeglinMods.Spectator.DI;

public static class ServiceRegistration
{
    public static IServiceContainer CreateAndConfigure(ManualLogSource log)
    {
        var container = new ServiceContainer();

        // Core
        container.RegisterSingleton<ManualLogSource>(log);

        // Spectator mode
        container.RegisterSingleton<ISpectatorMode>(new SpectatorMode());

        // Network protocol
        var typeRegistry = new MessageTypeRegistry();
        container.RegisterSingleton(typeRegistry);

        var serializer = new JsonNetworkSerializer(typeRegistry);
        container.RegisterSingleton<INetworkSerializer>(serializer);

        // Network transport
        var transport = new LiteNetTransport();
        container.RegisterSingleton<INetworkTransport>(transport);

        // Event registry
        var eventRegistry = new GameEventRegistry(serializer, transport, typeRegistry, log);
        container.RegisterSingleton<IGameEventRegistry>(eventRegistry);

        // Network host/client
        var host = new NetworkHost(transport, serializer);
        container.RegisterSingleton<IMessageSender>(host);

        var client = new NetworkClient(transport, eventRegistry, serializer);
        container.RegisterSingleton<IMessageReceiver>(client);

        // Identifiers
        container.RegisterSingleton(new EnemyIdentifier());
        container.RegisterSingleton(new OrbIdentifier());

        // Version checker
        var versionChecker = new VersionChecker(log);
        versionChecker.Check();
        container.RegisterSingleton(versionChecker);

        // Event discovery
        var eventDiscovery = new EventDiscovery(log);
        eventDiscovery.ScanGameDelegates();
        container.RegisterSingleton(eventDiscovery);

        // Register all event handler pairs (including handshake)
        RegisterAllHandlers(eventRegistry);

        // Log event discovery report
        foreach (var typeId in eventRegistry.RegisteredTypeIds)
            eventDiscovery.MarkRegistered(typeId);
        eventDiscovery.LogReport();

        // Subscribe to game events
        var spectatorMode = container.Resolve<ISpectatorMode>();
        var enemyId = container.Resolve<EnemyIdentifier>();
        var orbId = container.Resolve<OrbIdentifier>();
        SubscribeAll(eventRegistry, spectatorMode, enemyId, orbId, log);

        // Send handshake on connect
        transport.OnClientConnected += () =>
        {
            eventRegistry.Dispatch(new HandshakeEvent
            {
                ModVersion = SpectatorPluginInfo.VERSION,
                CompiledGameVersion = SpectatorPluginInfo.COMPILED_GAME_VERSION,
                RuntimeGameVersion = UnityEngine.Application.version ?? "unknown",
                IsHost = transport.IsHost,
                RegisteredHandlerCount = eventRegistry.RegisteredTypeIds.Count
            });
        };

        return container;
    }

    private static void RegisterAllHandlers(IGameEventRegistry registry)
    {
        // Handshake (version exchange)
        registry.Register(new HandshakeServerHandler(), new HandshakeClientHandler());

        // Battle events
        registry.Register(new BattleStartedServerHandler(), new BattleStartedClientHandler());
        registry.Register(new BattleEndedServerHandler(), new BattleEndedClientHandler());
        registry.Register(new VictoryServerHandler(), new VictoryClientHandler());
        registry.Register(new DefeatServerHandler(), new DefeatClientHandler());
        registry.Register(new AttackStartedServerHandler(), new AttackStartedClientHandler());
        registry.Register(new TurnCompleteServerHandler(), new TurnCompleteClientHandler());
        registry.Register(new ShotCompleteServerHandler(), new ShotCompleteClientHandler());
        registry.Register(new RoundIncrementedServerHandler(), new RoundIncrementedClientHandler());
        registry.Register(new ReloadStartedServerHandler(), new ReloadStartedClientHandler());
        registry.Register(new CritActivatedServerHandler(), new CritActivatedClientHandler());
        registry.Register(new CritDeactivatedServerHandler(), new CritDeactivatedClientHandler());
        registry.Register(new BombThrownServerHandler(), new BombThrownClientHandler());
        registry.Register(new BombDetonatedServerHandler(), new BombDetonatedClientHandler());
        registry.Register(new OrbDiscardedServerHandler(), new OrbDiscardedClientHandler());
        registry.Register(new AwaitingShotServerHandler(), new AwaitingShotClientHandler());
        registry.Register(new ShotTimeoutServerHandler(), new ShotTimeoutClientHandler());

        // Health events
        registry.Register(new PlayerDamagedServerHandler(), new PlayerDamagedClientHandler());
        registry.Register(new PlayerHealedServerHandler(), new PlayerHealedClientHandler());
        registry.Register(new ArmourHitServerHandler(), new ArmourHitClientHandler());
        registry.Register(new DodgeServerHandler(), new DodgeClientHandler());
        registry.Register(new HealthDepletedServerHandler(), new HealthDepletedClientHandler());
        registry.Register(new MaxHealthChangedServerHandler(), new MaxHealthChangedClientHandler());

        // Enemy events
        registry.Register(new EnemySpawnedServerHandler(), new EnemySpawnedClientHandler());
        registry.Register(new EnemyDamagedServerHandler(), new EnemyDamagedClientHandler());
        registry.Register(new EnemyDestroyedServerHandler(), new EnemyDestroyedClientHandler());
        registry.Register(new EnemyKilledServerHandler(), new EnemyKilledClientHandler());
        registry.Register(new EnemyAttackServerHandler(), new EnemyAttackClientHandler());
        registry.Register(new EnemyMovedServerHandler(), new EnemyMovedClientHandler());

        // Deck events
        registry.Register(new BallDrawnServerHandler(), new BallDrawnClientHandler());
        registry.Register(new BallUsedServerHandler(), new BallUsedClientHandler());
        registry.Register(new BallUpgradedServerHandler(), new BallUpgradedClientHandler());
        registry.Register(new DeckShuffledServerHandler(), new DeckShuffledClientHandler());

        // Relic events
        registry.Register(new RelicAddedServerHandler(), new RelicAddedClientHandler());
        registry.Register(new RelicRemovedServerHandler(), new RelicRemovedClientHandler());
        registry.Register(new RelicUsedServerHandler(), new RelicUsedClientHandler());

        // Currency events
        registry.Register(new GoldChangedServerHandler(), new GoldChangedClientHandler());

        // Status effect events
        registry.Register(new StatusEffectsUpdatedServerHandler(), new StatusEffectsUpdatedClientHandler());

        // Ball events
        registry.Register(new ShotFiredServerHandler(), new ShotFiredClientHandler());
        registry.Register(new BallWallBounceServerHandler(), new BallWallBounceClientHandler());
        registry.Register(new BallDestroyedServerHandler(), new BallDestroyedClientHandler());

        // Peg events
        registry.Register(new PegHitServerHandler(), new PegHitClientHandler());
        registry.Register(new PegActivatedServerHandler(), new PegActivatedClientHandler());
        registry.Register(new PegDestroyedServerHandler(), new PegDestroyedClientHandler());

        // Map events
        registry.Register(new NodeSelectedServerHandler(), new NodeSelectedClientHandler());
    }

    private static void SubscribeAll(
        IGameEventRegistry registry,
        ISpectatorMode spectatorMode,
        EnemyIdentifier enemyIdentifier,
        OrbIdentifier orbIdentifier,
        ManualLogSource log)
    {
        new BattleEventSubscriptions(registry, spectatorMode).Subscribe();
        new HealthSubscriptions(registry, log).Subscribe();
        new EnemySubscriptions(registry, enemyIdentifier, log).Subscribe();
        new DeckSubscriptions(registry, orbIdentifier, log).Subscribe();
        new RelicSubscriptions(registry, log).Subscribe();
        new CurrencySubscriptions(registry, log).Subscribe();
        new StatusEffectEventSubscriptions(registry, spectatorMode).Subscribe();
        new BallSubscriptions(registry, log).Subscribe();
        new PegSubscriptions(registry, log).Subscribe();
        new MapSubscriptions(registry, log).Subscribe();
    }
}
