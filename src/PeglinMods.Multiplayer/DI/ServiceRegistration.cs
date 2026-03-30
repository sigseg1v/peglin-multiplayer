using BepInEx.Logging;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.Events.Handlers;
using PeglinMods.Multiplayer.Events.Handlers.Ball;
using PeglinMods.Multiplayer.Events.Handlers.Battle;
using PeglinMods.Multiplayer.Events.Handlers.Currency;
using PeglinMods.Multiplayer.Events.Handlers.Deck;
using PeglinMods.Multiplayer.Events.Handlers.Enemy;
using PeglinMods.Multiplayer.Events.Handlers.Health;
using PeglinMods.Multiplayer.Events.Handlers.Map;
using PeglinMods.Multiplayer.Events.Handlers.Peg;
using PeglinMods.Multiplayer.Events.Handlers.Relic;
using PeglinMods.Multiplayer.Events.Handlers.State;
using PeglinMods.Multiplayer.Events.Handlers.StatusEffect;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Events.Network;
using PeglinMods.Multiplayer.Events.Network.Ball;
using PeglinMods.Multiplayer.Events.Network.Battle;
using PeglinMods.Multiplayer.Events.Network.Currency;
using PeglinMods.Multiplayer.Events.Network.Deck;
using PeglinMods.Multiplayer.Events.Network.Enemy;
using PeglinMods.Multiplayer.Events.Network.Health;
using PeglinMods.Multiplayer.Events.Network.Map;
using PeglinMods.Multiplayer.Events.Network.Peg;
using PeglinMods.Multiplayer.Events.Network.Relic;
using PeglinMods.Multiplayer.Events.Network.StatusEffect;
using PeglinMods.Multiplayer.Events.Subscriptions;
using PeglinMods.Multiplayer.Network;
using PeglinMods.Multiplayer.Network.Protocol;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Utility;

namespace PeglinMods.Multiplayer.DI;

public static class ServiceRegistration
{
    public static IServiceContainer CreateAndConfigure(ManualLogSource log)
    {
        log.LogInfo("[DI] Phase 1: Core services...");
        var container = Phase1_Core(log);

        log.LogInfo("[DI] Phase 2a: MessageTypeRegistry...");
        Phase2a_TypeRegistry(container);
        log.LogInfo("[DI] Phase 2b: JsonNetworkSerializer...");
        Phase2b_Serializer(container);
        log.LogInfo("[DI] Phase 2c: LiteNetTransport...");
        Phase2c_Transport(container);

        log.LogInfo("[DI] Phase 3: Events...");
        Phase3_Events(container, log);

        log.LogInfo("[DI] Phase 4: Handlers...");
        Phase4_Handlers(container);

        log.LogInfo("[DI] Phase 5: Subscriptions...");
        Phase5_Subscriptions(container, log);

        log.LogInfo("[DI] Phase 6: Handshake hook...");
        Phase6_Handshake(container);

        log.LogInfo("[DI] All phases complete.");
        return container;
    }

    private static ServiceContainer Phase1_Core(ManualLogSource log)
    {
        var container = new ServiceContainer();
        container.RegisterSingleton<ManualLogSource>(log);
        container.RegisterSingleton<IMultiplayerMode>(new MultiplayerMode());
        return container;
    }

    private static void Phase2a_TypeRegistry(ServiceContainer container)
    {
        var typeRegistry = new MessageTypeRegistry();
        container.RegisterSingleton(typeRegistry);
    }

    private static void Phase2b_Serializer(ServiceContainer container)
    {
        var typeRegistry = container.Resolve<MessageTypeRegistry>();
        var serializer = new JsonNetworkSerializer(typeRegistry);
        container.RegisterSingleton<INetworkSerializer>(serializer);
    }

    private static void Phase2c_Transport(ServiceContainer container)
    {
        var transport = new LiteNetTransport();
        container.RegisterSingleton<INetworkTransport>(transport);
    }

    private static void Phase3_Events(ServiceContainer container, ManualLogSource log)
    {
        var serializer = container.Resolve<INetworkSerializer>();
        var transport = container.Resolve<INetworkTransport>();
        var typeRegistry = container.Resolve<MessageTypeRegistry>();

        var eventRegistry = new GameEventRegistry(serializer, transport, typeRegistry, log);
        container.RegisterSingleton<IGameEventRegistry>(eventRegistry);
        container.RegisterSingleton(eventRegistry);

        var host = new NetworkHost(transport, serializer);
        container.RegisterSingleton<IMessageSender>(host);

        var client = new NetworkClient(transport, eventRegistry, serializer);
        container.RegisterSingleton<IMessageReceiver>(client);

        var enemyId = new EnemyIdentifier();
        container.RegisterSingleton(enemyId);
        var pegId = new PegIdentifier();
        container.RegisterSingleton(pegId);
        var orbId = new OrbIdentifier();
        container.RegisterSingleton(orbId);

        // Game state sync service (host -> captures state and sends)
        var syncService = new GameStateSyncService(log, eventRegistry, container.Resolve<IMultiplayerMode>(), enemyId, pegId, orbId);
        container.RegisterSingleton<IGameStateSyncService>(syncService);

        // Game state apply service (client -> receives state and applies)
        var applyService = new GameStateApplyService(log, enemyId, pegId);
        container.RegisterSingleton(applyService);

        var versionChecker = new VersionChecker(log);
        versionChecker.Check();
        container.RegisterSingleton(versionChecker);

        var eventDiscovery = new EventDiscovery(log);
        eventDiscovery.ScanGameDelegates();
        container.RegisterSingleton(eventDiscovery);
    }

    private static void Phase4_Handlers(ServiceContainer container)
    {
        var eventRegistry = container.Resolve<GameEventRegistry>();
        var eventDiscovery = container.Resolve<EventDiscovery>();

        RegisterAllHandlers(eventRegistry);

        foreach (var typeId in eventRegistry.RegisteredTypeIds)
            eventDiscovery.MarkRegistered(typeId);
        eventDiscovery.LogReport();
    }

    private static void Phase5_Subscriptions(ServiceContainer container, ManualLogSource log)
    {
        var eventRegistry = container.Resolve<IGameEventRegistry>();
        var multiplayerMode = container.Resolve<IMultiplayerMode>();
        var enemyId = container.Resolve<EnemyIdentifier>();
        var orbId = container.Resolve<OrbIdentifier>();
        var syncService = container.Resolve<IGameStateSyncService>();
        SubscribeAll(eventRegistry, multiplayerMode, enemyId, orbId, syncService, log);
    }

    private static void Phase6_Handshake(ServiceContainer container)
    {
        var transport = container.Resolve<INetworkTransport>();
        var eventRegistry = container.Resolve<GameEventRegistry>();

        var syncService = container.Resolve<IGameStateSyncService>();

        transport.OnClientConnected += () =>
        {
            eventRegistry.Dispatch(new HandshakeEvent
            {
                PlayerName = UI.MultiplayerUI.LocalPlayerName,
                ModVersion = MultiplayerPluginInfo.VERSION,
                CompiledGameVersion = MultiplayerPluginInfo.COMPILED_GAME_VERSION,
                RuntimeGameVersion = UnityEngine.Application.version ?? "unknown",
                IsHost = transport.IsHost,
                RegisteredHandlerCount = eventRegistry.RegisteredTypeIds.Count
            });

            // Send full game state to newly connected client
            if (transport.IsHost)
                syncService.SyncAll();
        };
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
        registry.Register(new DamageTextServerHandler(), new DamageTextClientHandler());

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
        registry.Register(new BallPositionServerHandler(), new BallPositionClientHandler());
        registry.Register(new AimUpdateServerHandler(), new AimUpdateClientHandler());

        // Peg events
        registry.Register(new PegHitServerHandler(), new PegHitClientHandler());
        registry.Register(new PegActivatedServerHandler(), new PegActivatedClientHandler());
        registry.Register(new PegDestroyedServerHandler(), new PegDestroyedClientHandler());

        // Map events
        registry.Register(new NodeSelectedServerHandler(), new NodeSelectedClientHandler());
        registry.Register(new NodeActivatedServerHandler(), new NodeActivatedClientHandler());

        // State sync snapshots
        registry.Register(new FullGameStateServerHandler(), new FullGameStateClientHandler());
        registry.Register(new MapStateSnapshotServerHandler(), new MapStateSnapshotClientHandler());
        registry.Register(new PlayerStateSnapshotServerHandler(), new PlayerStateSnapshotClientHandler());
        registry.Register(new EnemyStateSnapshotServerHandler(), new EnemyStateSnapshotClientHandler());
        registry.Register(new PegboardStateSnapshotServerHandler(), new PegboardStateSnapshotClientHandler());
        registry.Register(new DeckStateSnapshotServerHandler(), new DeckStateSnapshotClientHandler());
        registry.Register(new RelicStateSnapshotServerHandler(), new RelicStateSnapshotClientHandler());
    }

    private static void SubscribeAll(
        IGameEventRegistry registry,
        IMultiplayerMode multiplayerMode,
        EnemyIdentifier enemyIdentifier,
        OrbIdentifier orbIdentifier,
        IGameStateSyncService syncService,
        ManualLogSource log)
    {
        new BattleEventSubscriptions(registry, multiplayerMode).Subscribe();
        new HealthSubscriptions(registry, log).Subscribe();
        new EnemySubscriptions(registry, enemyIdentifier, log).Subscribe();
        new DeckSubscriptions(registry, orbIdentifier, log).Subscribe();
        new RelicSubscriptions(registry, log).Subscribe();
        new CurrencySubscriptions(registry, log).Subscribe();
        new StatusEffectEventSubscriptions(registry, multiplayerMode).Subscribe();
        new BallSubscriptions(registry, log).Subscribe();
        new PegSubscriptions(registry, log).Subscribe();
        new MapSubscriptions(registry, log).Subscribe();

        // State sync subscriptions - triggers full/partial state capture on key events
        new StateSyncSubscriptions(syncService, multiplayerMode, log).Subscribe();
    }
}
