using System;
using System.Linq;
using HarmonyLib;
using I2.Loc;
using Multipeglin.Events;
using UnityEngine;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class GameInitPatches
{
    [HarmonyPatch(typeof(GameInit), "Start")]
    [HarmonyPrefix]
    public static bool GameInit_Start_Prefix()
    {
        // In coop mode (lobby game start), allow GameInit so each player gets their own deck/relics
        if (UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        return !ShouldSuppressClientLogic;
    }

    /// <summary>
    /// After GameInit.Start() completes in coop mode, initialize per-player state
    /// in CoopStateManager, capture the host's initial state, and skip the relic
    /// selection screen by calling LoadMapScene() directly.
    /// </summary>
    [HarmonyPatch(typeof(GameInit), "Start")]
    [HarmonyPostfix]
    public static void GameInit_Start_Postfix(GameInit __instance)
    {
        // Only run when hosting or in coop mode
        if (!UI.LobbyUI.GameStartReceived)
        {
            return;
        }

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) != true)
        {
            return;
        }

        // Clear stale reward/relic-selection state from any previous run. Without this,
        // flags like HostHasChosenRelic and the various *PhaseActive bools persist from
        // the prior game and prevent the starting relic UI from advancing on the second run.
        Events.Handlers.Coop.CoopRewardState.Reset();

        var gameStartEvent = UI.LobbyUI.LatestGameStartEvent;
        if (gameStartEvent?.FinalPlayers != null)
        {
            // Initialize players only on the first run. On subsequent GameInit.Start()
            // calls (new runs), re-initialize to reset accumulated state.
            // Check if players already exist and match the expected count.
            var needsInit = coopState.TotalPlayerCount != gameStartEvent.FinalPlayers.Count;
            if (!needsInit)
            {
                foreach (var player in gameStartEvent.FinalPlayers)
                {
                    var existing = coopState.GetPlayerState(player.SlotIndex);
                    if (existing == null || !existing.IsInitialized)
                    {
                        needsInit = true;
                        break;
                    }
                }
            }

            if (needsInit)
            {
                foreach (var player in gameStartEvent.FinalPlayers)
                {
                    coopState.InitializePlayer(player.SlotIndex, player.ChosenClass, player.PlayerName);
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Initialized coop player: slot={player.SlotIndex}, name={player.PlayerName}, class={player.ChosenClass}");
                }
            }
            else
            {
                MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Players already initialized ({coopState.TotalPlayerCount}), re-capturing state");
            }

            // Debug-only: when MULTIPEGLIN_DEBUG is set, grant the host the two
            // easter-egg orbs so they can be tested without rolling them in shops.
            // Must run BEFORE CaptureInitialState so the snapshot includes them.
            if (IsHosting)
            {
                Debug.DebugStartingDeck.TryGrantHostDebugOrbs();
            }

            // Capture/re-capture host's state (slot 0) after GameInit has set up deck/relics/health.
            // This runs on every GameInit.Start() so the host's deck is always current.
            coopState.CaptureInitialState(0);
            coopState.ActivePlayerSlot = 0;

            // CaptureInitialState reads health from PlayerHealthController, which may not
            // exist on the PostMainMenu scene. Read directly from GameInit's FloatVariable
            // ScriptableObject references which ARE set after Start() completes.
            var hostState = coopState.GetPlayerState(0);
            if (hostState != null && hostState.MaxHealth <= 0)
            {
                try
                {
                    var hp = __instance.playerHealth?.Value ?? 0;
                    var maxHp = __instance.maxPlayerHealth?.Value ?? 0;
                    if (maxHp > 0)
                    {
                        hostState.CurrentHealth = hp;
                        hostState.MaxHealth = maxHp;
                        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Health from GameInit FloatVars: hp={hp}/{maxHp}");
                    }
                }
                catch (System.Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Failed to read health from GameInit: {ex.Message}");
                }
            }

            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Coop: captured host initial state, ActivePlayerSlot=0, " +
                $"{gameStartEvent.FinalPlayers.Count} players, hp={hostState?.CurrentHealth}/{hostState?.MaxHealth}, " +
                $"deck={hostState?.CompleteDeck.Count}");

            // Build starting state for non-host players from ClassLoadoutData.
            // The host's singletons contain the host's data, so we can't use
            // CaptureInitialState for other slots. Instead, directly populate
            // each non-host player's CoopPlayerState from their class loadout.
            // Only on first initialization — skip if player already has state.
            foreach (var player in gameStartEvent.FinalPlayers)
            {
                if (player.IsHost)
                {
                    continue;
                }

                var playerState = coopState.GetPlayerState(player.SlotIndex);
                if (playerState == null)
                {
                    continue;
                }

                // Skip if player already has initialized state (re-capture, not re-init)
                if (playerState.IsInitialized && playerState.CompleteDeck.Count > 0)
                {
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Slot {player.SlotIndex} already initialized with {playerState.CompleteDeck.Count} orbs, skipping re-init");
                    continue;
                }

                // All players start with the same max HP as the host
                var maxHp = hostState?.MaxHealth ?? (__instance.maxPlayerHealth?.Value ?? 0);
                playerState.CurrentHealth = maxHp; // Full health at start
                playerState.MaxHealth = maxHp;

                // Build starting deck from ClassLoadoutData
                var targetClass = (Peglin.ClassSystem.Class)player.ChosenClass;
                var classLoadouts = StaticGameData.classLoadouts;
                Peglin.ClassSystem.ClassLoadoutData loadout = null;
                if (classLoadouts != null)
                {
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Slot {player.SlotIndex}: searching {classLoadouts.Length} classLoadouts for class {targetClass} (int={player.ChosenClass})");
                    foreach (var pair in classLoadouts)
                    {
                        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches]   classLoadout: {pair.Class} orbs={pair.Loadout?.StartingOrbs?.Count ?? 0}");
                        if (pair.Class == targetClass)
                        {
                            loadout = pair.Loadout;
                            break;
                        }
                    }
                }
                else
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Slot {player.SlotIndex}: StaticGameData.classLoadouts is NULL — cannot look up {targetClass}");
                }

                if (loadout?.StartingOrbs != null)
                {
                    playerState.CompleteDeck.Clear();
                    foreach (var orb in loadout.StartingOrbs)
                    {
                        if (orb == null)
                        {
                            continue;
                        }

                        playerState.CompleteDeck.Add(new GameState.SerializedOrb
                        {
                            PrefabName = orb.name,
                            Level = 0,
                        });
                    }

                    var deckNames = string.Join(", ", playerState.CompleteDeck.Select(o => o.PrefabName));
                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Slot {player.SlotIndex}: built deck from {targetClass} ClassLoadoutData: [{deckNames}]");
                }
                else
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Slot {player.SlotIndex}: ClassLoadoutData for {targetClass} has NO StartingOrbs (loadout={loadout != null})");
                }

                if (loadout?.StartingRelics != null)
                {
                    playerState.OwnedRelics.Clear();
                    foreach (var relic in loadout.StartingRelics)
                    {
                        if (relic == null)
                        {
                            continue;
                        }

                        playerState.OwnedRelics.Add(new GameState.SerializedRelic
                        {
                            Effect = (int)relic.effect,
                            LocKey = relic.locKey ?? string.Empty,
                            Rarity = (int)relic.globalRarity,
                        });
                    }
                }

                // Apply cruciball starting-deck penalties to non-host players. The
                // host's slot already has these added by GameInit.Start (native
                // code reads CruciballManager.AdditionalStarterStones / ShouldAdd*),
                // but non-host decks here are built straight from ClassLoadoutData
                // and would otherwise miss the stone/Horriball/Terriball additions
                // entirely. Re-applies the same rules so every player sees the
                // same penalties as the host at their cruciball level.
                try
                {
                    var cms = UnityEngine.Resources.FindObjectsOfTypeAll<Cruciball.CruciballManager>();
                    var cm = (cms != null && cms.Length > 0) ? cms[0] : null;
                    if (cm != null)
                    {
                        var extraStones = cm.AdditionalStarterStones();
                        if (extraStones > 0 && cm.stonePrefab != null)
                        {
                            var stoneName = cm.stonePrefab.gameObject.name;
                            for (var s = 0; s < extraStones; s++)
                            {
                                playerState.CompleteDeck.Add(new GameState.SerializedOrb
                                {
                                    PrefabName = stoneName,
                                    Level = 0,
                                });
                            }

                            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Slot {player.SlotIndex}: cruciball added {extraStones}x {stoneName}");
                        }

                        if (cm.ShouldAddCursedOrb() && cm.dudOrb1Prefab != null)
                        {
                            var dudName = cm.dudOrb1Prefab.gameObject.name;
                            playerState.CompleteDeck.Add(new GameState.SerializedOrb { PrefabName = dudName, Level = 0 });
                            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Slot {player.SlotIndex}: cruciball added cursed orb {dudName}");
                        }

                        if (cm.ShouldAddUnremovableCursedOrb() && cm.unremovalbeDudOrb1Prefab != null)
                        {
                            var unrDudName = cm.unremovalbeDudOrb1Prefab.gameObject.name;
                            playerState.CompleteDeck.Add(new GameState.SerializedOrb { PrefabName = unrDudName, Level = 0 });
                            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Slot {player.SlotIndex}: cruciball added unremovable cursed orb {unrDudName}");
                        }
                    }
                    else
                    {
                        MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Slot {player.SlotIndex}: no CruciballManager — skipping cruciball deck penalties");
                    }
                }
                catch (System.Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Slot {player.SlotIndex}: cruciball deck penalties failed: {ex.Message}");
                }

                playerState.IsInitialized = true;
                MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Built slot {player.SlotIndex} state from ClassLoadoutData: " +
                    $"hp={playerState.CurrentHealth}/{playerState.MaxHealth}, deck={playerState.CompleteDeck.Count}, relics={playerState.OwnedRelics.Count}");

                // On the CLIENT: only add starting class relics for OUR OWN slot.
                // Without the slot guard, the foreach over FinalPlayers would add every
                // non-host player's starter relic into every client's local RelicManager
                // — so PEGLIN2/3/4 all ended up holding Roundreloquence + Balladroit +
                // Peglintuition simultaneously. That also leaks reward-orb-count effects
                // (e.g. ADDITIONAL_PEGLIN_CHOICES, Eye of Turtle) to clients who don't
                // own them, since BattleUpgradeCanvas reads from the local RelicManager.
                var isMyOwnSlot = false;
                if (!IsHosting && services.TryResolve<Multiplayer.PlayerRegistry>(out var localRegistry)
                    && localRegistry.LocalSlot != null
                    && localRegistry.LocalSlot.SlotIndex == player.SlotIndex)
                {
                    isMyOwnSlot = true;
                }

                if (isMyOwnSlot && loadout?.StartingRelics != null)
                {
                    try
                    {
                        var clientRelicMgrs = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                        if (clientRelicMgrs != null && clientRelicMgrs.Length > 0)
                        {
                            foreach (var relic in loadout.StartingRelics)
                            {
                                if (relic == null)
                                {
                                    continue;
                                }

                                try
                                {
                                    AllowRelicSync = true;
                                    clientRelicMgrs[0].AddRelic(relic);
                                    MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Client slot {player.SlotIndex}: added starting class relic {relic.effect} ({relic.locKey})");
                                }
                                catch
                                {
                                }
                                finally { AllowRelicSync = false; }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        // Continuing a saved run: the native continue path (LoadData.NewGame == false)
        // already called LoadMapScene() inside Start(), and nobody gets a starting
        // relic. Entering the relic-selection phase here would send bogus
        // RelicChoicesEvents to clients (black overlay demanding a pick) and leave
        // HostRelicSelectionActive stuck true, deadlocking later wait-for-all phases.
        if (gameStartEvent?.IsContinue == true || Continue.ContinueSession.IsActive)
        {
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Continue run — skipping starting relic selection phase");
            return;
        }

        // Coop relic selection: both host and clients choose a starting relic.
        // The host sees the game's native relic canvas; clients see CoopRewardUI.
        // LoadMapScene is blocked until all players have chosen.
        if (IsHosting)
        {
            // Initialize relic selection tracking state
            var nonHostCount = 0;
            if (gameStartEvent?.FinalPlayers != null)
            {
                foreach (var p in gameStartEvent.FinalPlayers)
                {
                    if (!p.IsHost)
                    {
                        nonHostCount++;
                    }
                }
            }

            Events.Handlers.Coop.CoopRewardState.HostRelicSelectionActive = true;
            Events.Handlers.Coop.CoopRewardState.HostHasChosenRelic = false;
            Events.Handlers.Coop.CoopRewardState.TotalClientsExpected = nonHostCount;
            Events.Handlers.Coop.CoopRewardState.ClientRelicChoicesReceived.Clear();
            Events.Handlers.Coop.CoopRewardState.PendingGameInitInstance = __instance;

            MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Coop host: entering relic selection phase, waiting for {nonHostCount} client(s)");

            // Generate starting relic choices for each non-host player and send
            if (services.TryResolve<IGameEventRegistry>(out var registry) && gameStartEvent?.FinalPlayers != null)
            {
                var relicMgrs = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                if (relicMgrs != null && relicMgrs.Length > 0)
                {
                    var rm = relicMgrs[0];
                    foreach (var player in gameStartEvent.FinalPlayers)
                    {
                        if (player.IsHost)
                        {
                            continue;
                        }

                        var choices = new System.Collections.Generic.List<GameState.Snapshots.RelicEntry>();
                        try
                        {
                            var relics = rm.GetMultipleRelicsOffOfQueue(3, Relics.RelicRarity.COMMON);
                            foreach (var relic in relics)
                            {
                                var displayName = string.Empty;
                                try
                                {
                                    displayName = LocalizationManager.GetTranslation(relic.nameKey);
                                    if (string.IsNullOrEmpty(displayName))
                                    {
                                        displayName = relic.englishDisplayName ?? relic.locKey ?? "Unknown";
                                    }
                                }
                                catch
                                {
                                    displayName = relic.englishDisplayName ?? relic.locKey ?? "Unknown";
                                }

                                var description = string.Empty;
                                try
                                {
                                    description = LocalizationManager.GetTranslation(relic.descKey);
                                    if (string.IsNullOrEmpty(description))
                                    {
                                        description = relic.locKey ?? string.Empty;
                                    }
                                }
                                catch
                                {
                                    description = relic.locKey ?? string.Empty;
                                }

                                choices.Add(new GameState.Snapshots.RelicEntry
                                {
                                    Effect = (int)relic.effect,
                                    EffectName = displayName,
                                    LocKey = description,
                                    Rarity = (int)relic.globalRarity,
                                    IsEnabled = true,
                                });
                            }
                        }
                        catch (Exception ex2)
                        {
                            MultiplayerPlugin.Logger?.LogWarning($"[GameInit] Failed to generate relic choices for slot {player.SlotIndex}: {ex2.Message}");
                        }

                        if (choices.Count > 0)
                        {
                            registry.Dispatch(new Events.Network.Coop.RelicChoicesEvent
                            {
                                TargetSlotIndex = player.SlotIndex,
                                Choices = choices,
                            });
                            MultiplayerPlugin.Logger?.LogInfo($"[GameInit] Sent {choices.Count} relic choices to slot {player.SlotIndex}");
                        }
                    }

                    // Also generate relic choices for the host and display via CoopRewardUI
                    try
                    {
                        var hostChoices = new System.Collections.Generic.List<GameState.Snapshots.RelicEntry>();
                        var relics2 = rm.GetMultipleRelicsOffOfQueue(3, Relics.RelicRarity.COMMON);
                        foreach (var relic in relics2)
                        {
                            var displayName2 = string.Empty;
                            try
                            {
                                displayName2 = LocalizationManager.GetTranslation(relic.nameKey);
                                if (string.IsNullOrEmpty(displayName2))
                                {
                                    displayName2 = relic.englishDisplayName ?? relic.locKey ?? "Unknown";
                                }
                            }
                            catch
                            {
                                displayName2 = relic.englishDisplayName ?? relic.locKey ?? "Unknown";
                            }

                            var description2 = string.Empty;
                            try
                            {
                                description2 = LocalizationManager.GetTranslation(relic.descKey);
                                if (string.IsNullOrEmpty(description2))
                                {
                                    description2 = relic.locKey ?? string.Empty;
                                }
                            }
                            catch
                            {
                                description2 = relic.locKey ?? string.Empty;
                            }

                            hostChoices.Add(new GameState.Snapshots.RelicEntry
                            {
                                Effect = (int)relic.effect,
                                EffectName = displayName2,
                                LocKey = description2,
                                Rarity = (int)relic.globalRarity,
                                IsEnabled = true,
                            });
                        }

                        Events.Handlers.Coop.CoopRewardState.PendingRelicChoices = new Events.Network.Coop.RelicChoicesEvent
                        {
                            TargetSlotIndex = 0,
                            Choices = hostChoices,
                        };
                        MultiplayerPlugin.Logger?.LogInfo($"[GameInit] Generated {hostChoices.Count} relic choices for host (slot 0)");
                    }
                    catch (Exception ex4)
                    {
                        MultiplayerPlugin.Logger?.LogWarning($"[GameInit] Failed to generate host relic choices: {ex4.Message}");
                    }
                }
            }

            // Hide the native relic canvas — host uses CoopRewardUI like clients
            try
            {
                var canvasField = typeof(GameInit).GetField("_chooseRelicCanvas",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var canvasObj = canvasField?.GetValue(__instance) as GameObject;
                if (canvasObj != null)
                {
                    canvasObj.SetActive(false);
                    MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Coop host: hid native relic canvas, using CoopRewardUI");
                }
            }
            catch (Exception ex5)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Failed to hide host relic canvas: {ex5.Message}");
            }
        }
        else if (!IsHosting)
        {
            // Client: hide the game's native relic canvas — the client uses CoopRewardUI instead
            try
            {
                var canvasField = typeof(GameInit).GetField("_chooseRelicCanvas",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var canvasObj = canvasField?.GetValue(__instance) as GameObject;
                if (canvasObj != null)
                {
                    canvasObj.SetActive(false);
                    MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] Coop client: hid native relic canvas, using CoopRewardUI");
                }
            }
            catch (Exception ex3)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Failed to hide relic canvas: {ex3.Message}");
            }
        }
    }

    // =========================================================================
    // GATE LoadMapScene ON ALL RELIC CHOICES — host waits for clients
    // =========================================================================

    /// <summary>
    /// Intercept GameInit.LoadMapScene during coop relic selection.
    /// When the host chooses their relic, the game calls LoadMapScene via the
    /// SkipRelic tween callback. We block it until all clients have also chosen.
    /// </summary>
    [HarmonyPatch(typeof(GameInit), "LoadMapScene")]
    [HarmonyPrefix]
    public static bool GameInit_LoadMapScene_Prefix()
    {
        // Only intercept during coop relic selection
        if (!Events.Handlers.Coop.CoopRewardState.HostRelicSelectionActive)
        {
            return true;
        }

        if (!IsHosting)
        {
            return true;
        }

        // Host has chosen their relic -- mark it
        Events.Handlers.Coop.CoopRewardState.HostHasChosenRelic = true;

        // Check if all clients have also chosen
        if (Events.Handlers.Coop.CoopRewardState.AllClientRelicChoicesReceived)
        {
            // Everyone done -- allow LoadMapScene to proceed
            Events.Handlers.Coop.CoopRewardState.HostRelicSelectionActive = false;
            Events.Handlers.Coop.CoopRewardState.AllChoicesComplete = true;
            Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = false;
            MultiplayerPlugin.Logger?.LogInfo("[ClientPatches] All relic choices received -- proceeding to map");

            // Dispatch AllChoicesCompleteEvent
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IGameEventRegistry>(out var registry) == true)
            {
                registry.Dispatch(new Events.Network.Coop.AllChoicesCompleteEvent { Phase = "starting_relic" });
            }

            return true; // Allow LoadMapScene
        }

        // Not all clients have chosen yet -- block and show waiting
        MultiplayerPlugin.Logger?.LogInfo($"[ClientPatches] Host chose relic, waiting for " +
            $"{Events.Handlers.Coop.CoopRewardState.TotalClientsExpected - Events.Handlers.Coop.CoopRewardState.ClientRelicChoicesReceived.Count} more client(s)");
        Events.Handlers.Coop.CoopRewardState.WaitingForOtherPlayers = true;
        return false; // Block LoadMapScene until all done
    }
}
