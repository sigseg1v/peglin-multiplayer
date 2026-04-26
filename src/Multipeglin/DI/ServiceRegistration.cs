using BepInEx.Logging;
using Multipeglin.Events;
using Multipeglin.Events.Handlers;
using Multipeglin.Events.Handlers.Ball;
using Multipeglin.Events.Handlers.Battle;
using Multipeglin.Events.Handlers.Cursor;
using Multipeglin.Events.Handlers.Currency;
using Multipeglin.Events.Handlers.Deck;
using Multipeglin.Events.Handlers.Enemy;
using Multipeglin.Events.Handlers.Health;
using CoopHandlers = Multipeglin.Events.Handlers.Coop;
using LobbyHandlers = Multipeglin.Events.Handlers.Lobby;
using ScenarioHandlers = Multipeglin.Events.Handlers.Scenarios;
using Multipeglin.Events.Handlers.Map;
using Multipeglin.Events.Handlers.Peg;
using Multipeglin.Events.Handlers.Relic;
using Multipeglin.Events.Handlers.State;
using Multipeglin.Events.Handlers.StatusEffect;
using Multipeglin.GameState;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Events.Network;
using Multipeglin.Events.Network.Ball;
using Multipeglin.Events.Network.Battle;
using Multipeglin.Events.Network.Currency;
using Multipeglin.Events.Network.Deck;
using Multipeglin.Events.Network.Enemy;
using Multipeglin.Events.Network.Health;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Events.Network.Lobby;
using Multipeglin.Events.Network.Scenarios;
using Multipeglin.Events.Network.Map;
using Multipeglin.Events.Network.Peg;
using Multipeglin.Events.Network.Relic;
using Multipeglin.Events.Network.StatusEffect;
using Multipeglin.Events.Subscriptions;
using Multipeglin.Network;
using Multipeglin.Network.Protocol;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;

namespace Multipeglin.DI;

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
        log.LogInfo("[DI] Phase 2c: Transport...");
        Phase2c_Transport(container, log);

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

    private static void Phase2c_Transport(ServiceContainer container, ManualLogSource log)
    {
        // SteamTransport is attached later from MultiplayerUI.Start (PreMainMenu scene).
        // Touching SteamManager.Initialized here — during plugin Awake, before any scene
        // loads — auto-instantiates a SteamManager GameObject that doesn't survive, and
        // leaves Steamworks in an uninitialized state by the time the user clicks Host.
        var lite = new LiteNetTransport();
        var router = new TransportRouter(lite, null);
        container.RegisterSingleton<TransportRouter>(router);
        container.RegisterSingleton<INetworkTransport>(router);
        container.RegisterSingleton<ISteamTransport>(router);
        log.LogInfo("[DI] Transport router registered (Steam will attach at UI start if available)");
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
        var ballId = new BallIdentifier();
        container.RegisterSingleton(ballId);

        // Player registry for co-op lobby
        var playerRegistry = new PlayerRegistry();
        container.RegisterSingleton(playerRegistry);

        // Co-op state manager (host-side per-player state)
        var coopStateManager = new CoopStateManager(log, playerRegistry);
        coopStateManager.SetOrbIdentifier(orbId);
        container.RegisterSingleton(coopStateManager);

        // Turn manager (host-side turn order tracking)
        var turnManager = new TurnManager(log, coopStateManager);
        container.RegisterSingleton(turnManager);

        // Game state sync service (host -> captures state and sends)
        var syncService = new GameStateSyncService(log, eventRegistry, container.Resolve<IMultiplayerMode>(), enemyId, pegId, orbId, coopStateManager);
        container.RegisterSingleton<IGameStateSyncService>(syncService);

        // Game state apply service (client -> receives state and applies)
        var applyService = new GameStateApplyService(log, enemyId, pegId, orbId);
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
        var coopStateManager = container.Resolve<CoopStateManager>();
        var turnManager = container.Resolve<TurnManager>();
        SubscribeAll(eventRegistry, multiplayerMode, enemyId, orbId, syncService, coopStateManager, turnManager, log);
    }

    private static void Phase6_Handshake(ServiceContainer container)
    {
        var transport = container.Resolve<INetworkTransport>();
        var eventRegistry = container.Resolve<GameEventRegistry>();
        var log = container.Resolve<ManualLogSource>();

        var syncService = container.Resolve<IGameStateSyncService>();

        transport.OnClientConnected += peerId =>
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
                syncService.SyncAll("ClientHandshake");
        };

        // Host-side: handle client disconnects during battle
        var playerRegistry = container.Resolve<PlayerRegistry>();
        transport.OnDisconnected += peerId =>
        {
            if (!transport.IsHost) return;

            var slot = playerRegistry.GetSlotByPeerId(peerId);
            if (slot == null)
            {
                log.LogWarning($"[Disconnect] Peer {peerId} disconnected but no slot found in registry");
                return;
            }

            log.LogInfo($"[Disconnect] Peer {peerId} disconnected: slot {slot.SlotIndex} ({slot.PlayerName})");

            // Notify the turn system / coop subscriptions
            var coopSubs = CoopSubscriptions.Instance;
            if (coopSubs != null)
            {
                coopSubs.HandlePlayerDisconnect(slot.SlotIndex, slot.PlayerName);
            }
            else
            {
                log.LogWarning($"[Disconnect] CoopSubscriptions.Instance is null — cannot handle turn removal for slot {slot.SlotIndex}");
            }

            // Remove from player registry (after coop handling so the slot lookup works)
            playerRegistry.RemoveByPeerId(peerId);
            log.LogInfo($"[Disconnect] Removed peer {peerId} from PlayerRegistry. Remaining slots: {playerRegistry.SlotCount}");
        };
    }

    private static void RegisterAllHandlers(IGameEventRegistry registry)
    {
        // Handshake (version exchange) & disconnect
        registry.Register(new HandshakeServerHandler(), new HandshakeClientHandler());
        registry.Register(new DisconnectServerHandler(), new DisconnectClientHandler());

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
        registry.Register(new AnimationSyncServerHandler(), new AnimationSyncClientHandler());
        registry.Register(new SpiritOfRadiaPhaseTransitionServerHandler(), new SpiritOfRadiaPhaseTransitionClientHandler());

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
        registry.Register(new BallStateSnapshotServerHandler(), new BallStateSnapshotClientHandler());
        registry.Register(new AimUpdateServerHandler(), new AimUpdateClientHandler());

        // Cursor sync (bidirectional, via IMessageSender)
        registry.Register(new CursorPositionServerHandler(), new CursorPositionClientHandler());

        // Peg events
        registry.Register(new PegHitServerHandler(), new PegHitClientHandler());
        registry.Register(new PegActivatedServerHandler(), new PegActivatedClientHandler());
        registry.Register(new PegDestroyedServerHandler(), new PegDestroyedClientHandler());

        // Map events
        registry.Register(new NodeSelectedServerHandler(), new NodeSelectedClientHandler());
        registry.Register(new NodeActivatedServerHandler(), new NodeActivatedClientHandler());

        // Lobby events
        registry.Register(new LobbyHandlers.LobbyStateServerHandler(), new LobbyHandlers.LobbyStateClientHandler());
        registry.Register(new LobbyHandlers.ClassSelectServerHandler(), new LobbyHandlers.ClassSelectClientHandler());
        registry.Register(new LobbyHandlers.ReadyServerHandler(), new LobbyHandlers.ReadyClientHandler());
        registry.Register(new LobbyHandlers.GameStartServerHandler(), new LobbyHandlers.GameStartClientHandler());

        // Co-op turn system events
        registry.Register(new CoopHandlers.TurnChangeServerHandler(), new CoopHandlers.TurnChangeClientHandler());
        registry.Register(new CoopHandlers.ShootRequestServerHandler(), new CoopHandlers.ShootRequestClientHandler());
        registry.Register(new CoopHandlers.TargetSelectServerHandler(), new CoopHandlers.TargetSelectClientHandler());
        registry.Register(new CoopHandlers.PendingDamagePreviewServerHandler(), new CoopHandlers.PendingDamagePreviewClientHandler());

        // Co-op relic/reward selection events
        registry.Register(new CoopHandlers.RelicChoicesServerHandler(), new CoopHandlers.RelicChoicesClientHandler());
        registry.Register(new CoopHandlers.RelicChoiceServerHandler(), new CoopHandlers.RelicChoiceClientHandler());
        registry.Register(new CoopHandlers.RewardChoicesServerHandler(), new CoopHandlers.RewardChoicesClientHandler());
        registry.Register(new CoopHandlers.RewardChoiceServerHandler(), new CoopHandlers.RewardChoiceClientHandler());
        registry.Register(new CoopHandlers.AllChoicesCompleteServerHandler(), new CoopHandlers.AllChoicesCompleteClientHandler());
        registry.Register(new CoopHandlers.PostBattleStartServerHandler(), new CoopHandlers.PostBattleStartClientHandler());
        registry.Register(new CoopHandlers.PostBattleCompleteServerHandler(), new CoopHandlers.PostBattleCompleteClientHandler());
        registry.Register(new CoopHandlers.PostBattleGoldSpentServerHandler(), new CoopHandlers.PostBattleGoldSpentClientHandler());
        registry.Register(new CoopHandlers.PostBattleRelicChoicesServerHandler(), new CoopHandlers.PostBattleRelicChoicesClientHandler());
        registry.Register(new CoopHandlers.CoopOrbRewardChoicesServerHandler(), new CoopHandlers.CoopOrbRewardChoicesClientHandler());
        registry.Register(new CoopHandlers.RunStatsSnapshotServerHandler(), new CoopHandlers.RunStatsSnapshotClientHandler());
        registry.Register(new CoopHandlers.OrbDiscardRequestServerHandler(), new CoopHandlers.OrbDiscardRequestClientHandler());
        registry.Register(new CoopHandlers.SkipTurnRequestServerHandler(), new CoopHandlers.SkipTurnRequestClientHandler());

        // Scenario events (TextScenario / Mirror / Shop / Treasure)
        registry.Register(new ScenarioHandlers.MirrorEventStartServerHandler(), new ScenarioHandlers.MirrorEventStartClientHandler());
        registry.Register(new ScenarioHandlers.MirrorEventCompleteServerHandler(), new ScenarioHandlers.MirrorEventCompleteClientHandler());
        registry.Register(new ScenarioHandlers.TextScenarioCompleteServerHandler(), new ScenarioHandlers.TextScenarioCompleteClientHandler());
        registry.Register(new ScenarioHandlers.ShopCompleteServerHandler(), new ScenarioHandlers.ShopCompleteClientHandler());
        registry.Register(new ScenarioHandlers.ShopPurchaseServerHandler(), new ScenarioHandlers.ShopPurchaseClientHandler());
        registry.Register(new ScenarioHandlers.TreasureCompleteServerHandler(), new ScenarioHandlers.TreasureCompleteClientHandler());
        registry.Register(new ScenarioHandlers.PegMinigameCompleteServerHandler(), new ScenarioHandlers.PegMinigameCompleteClientHandler());

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
        CoopStateManager coopStateManager,
        TurnManager turnManager,
        ManualLogSource log)
    {
        new BattleEventSubscriptions(registry, multiplayerMode).Subscribe();
        new HealthSubscriptions(registry, log, coopStateManager).Subscribe();
        new EnemySubscriptions(registry, enemyIdentifier, log, coopStateManager).Subscribe();
        new DeckSubscriptions(registry, orbIdentifier, log).Subscribe();
        new RelicSubscriptions(registry, log).Subscribe();
        new CurrencySubscriptions(registry, log, coopStateManager).Subscribe();
        new StatusEffectEventSubscriptions(registry, multiplayerMode).Subscribe();
        new BallSubscriptions(registry, log).Subscribe();
        new PegSubscriptions(registry, log).Subscribe();
        new MapSubscriptions(registry, log).Subscribe();

        // Co-op damage distribution + turn system - enemy damage applies to all players,
        // TurnManager drives turn order during co-op battles
        new CoopSubscriptions(multiplayerMode, coopStateManager, turnManager, syncService, log).Subscribe();

        // State sync subscriptions - triggers full/partial state capture on key events
        new StateSyncSubscriptions(syncService, multiplayerMode, log).Subscribe();
    }
}
