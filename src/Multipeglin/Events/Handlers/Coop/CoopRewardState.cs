using System.Collections.Generic;
using Multipeglin.Events.Network.Coop;

namespace Multipeglin.Events.Handlers.Coop;

/// <summary>
/// Static holder for pending relic/reward choice data received from the host.
/// The UI layer reads from here to display selection overlays.
/// </summary>
public static class CoopRewardState
{
    /// <summary>Pending relic choices sent by the host for this player to pick from.</summary>
    public static RelicChoicesEvent PendingRelicChoices;

    /// <summary>Pending post-battle reward choices sent by the host for this player to pick from.</summary>
    public static RewardChoicesEvent PendingRewardChoices;

    /// <summary>True when this player has made their choice and is waiting for others.</summary>
    public static bool WaitingForOtherPlayers;

    /// <summary>True when the host has confirmed all players have finished choosing.</summary>
    public static bool AllChoicesComplete;

    /// <summary>True when we're in the initial relic selection phase.</summary>
    public static bool HostRelicSelectionActive;

    /// <summary>True when the host has picked their starting relic (but may still be waiting for clients).</summary>
    public static bool HostHasChosenRelic;

    /// <summary>Slot indices of clients who have sent their relic choice.</summary>
    public static System.Collections.Generic.HashSet<int> ClientRelicChoicesReceived = new System.Collections.Generic.HashSet<int>();

    /// <summary>Number of non-host players expected to choose.</summary>
    public static int TotalClientsExpected;

    /// <summary>Stored GameInit instance so we can call LoadMapScene after all choices received.</summary>
    public static object PendingGameInitInstance;

    /// <summary>True when all clients have made their relic choice.</summary>
    public static bool AllClientRelicChoicesReceived => TotalClientsExpected > 0 && ClientRelicChoicesReceived.Count >= TotalClientsExpected;

    // --- Native post-battle reward phase (replaces custom RewardChoicesEvent flow) ---

    /// <summary>True on client while the native BattleUpgradeCanvas is open for post-battle rewards.</summary>
    public static bool ClientInNativeRewardPhase;

    /// <summary>True on host when the coop post-battle reward phase is active (host + clients picking rewards).</summary>
    public static bool HostRewardPhaseActive;

    /// <summary>True on host when the host has finished its own post-battle rewards.</summary>
    public static bool HostRewardsDone;

    /// <summary>Stored PostBattleController reference so we can call StartNavigation later on the host.</summary>
    public static global::Battle.PostBattleController PendingPostBattleController;

    // --- Host-side: post-battle reward tracking ---

    /// <summary>Host-side: reward options sent to each slot (keyed by slot index).</summary>
    public static System.Collections.Generic.Dictionary<int, RewardChoicesEvent> PendingSentRewardChoices
        = new System.Collections.Generic.Dictionary<int, RewardChoicesEvent>();

    /// <summary>Host-side: slot indices of clients who have sent their post-battle reward choice.</summary>
    public static System.Collections.Generic.HashSet<int> ClientRewardChoicesReceived
        = new System.Collections.Generic.HashSet<int>();

    /// <summary>Host-side: number of non-host players expected to choose post-battle rewards.</summary>
    public static int TotalRewardClientsExpected;

    /// <summary>Host-side: true when all clients have made their post-battle reward choice.</summary>
    public static bool AllClientRewardChoicesReceived => TotalRewardClientsExpected > 0
        && ClientRewardChoicesReceived.Count >= TotalRewardClientsExpected;

    /// <summary>
    /// Per-slot orb choices for the post-battle "Add Orb" suggestion panel.
    /// Host: filled for ALL slots (including its own slot 0) before broadcasting
    /// PostBattleStartEvent. Client: filled only for its own slot via
    /// CoopOrbRewardChoicesClientHandler. Read by the
    /// PopulateSuggestionOrbs.GenerateAddableOrbs patch to override the seeded
    /// roll so each player sees independently-rolled orbs.
    /// </summary>
    public static System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<string>> PerSlotOrbChoices
        = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<string>>();

    /// <summary>
    /// Per-slot treasure relic choices for the "?" / treasure room. Host: rolls
    /// each slot's relic in the SetupRelicGrant Postfix and broadcasts via
    /// CoopTreasureRelicChoiceEvent. Client: filled only for its own slot via
    /// CoopTreasureRelicChoiceClientHandler. Read by the SetupRelicGrant patch
    /// to override the local roll so each player sees an independently-rolled
    /// relic instead of the same one (UnityEngine.Random shares seed).
    /// </summary>
    public static System.Collections.Generic.Dictionary<int, string> PerSlotTreasureRelics
        = new System.Collections.Generic.Dictionary<int, string>();

    // --- Shop wait-for-all ---

    /// <summary>True when the shop phase is active (all players shopping).</summary>
    public static bool ShopPhaseActive;

    /// <summary>True when the host has clicked "Exit Store".</summary>
    public static bool HostShopDone;

    /// <summary>Slot indices of clients who have sent their ShopCompleteEvent.</summary>
    public static System.Collections.Generic.HashSet<int> ClientShopChoicesReceived = new System.Collections.Generic.HashSet<int>();

    /// <summary>Number of non-host players expected to finish shopping.</summary>
    public static int TotalShopClientsExpected;

    /// <summary>True when all clients have finished shopping.</summary>
    public static bool AllClientShopChoicesReceived => TotalShopClientsExpected > 0
        && ClientShopChoicesReceived.Count >= TotalShopClientsExpected;

    /// <summary>Stored ShopManager reference so host can call CloseStore after all clients finish.</summary>
    public static object PendingShopManager;

    /// <summary>Host-side: set once the "all players done" branch has fired, to prevent
    /// duplicate AllChoicesComplete dispatches and repeated CloseStore calls when the
    /// client spams ShopCompleteEvents.</summary>
    public static bool ShopCompletionProceeded;

    /// <summary>Client-side: true after the client has sent their ShopCompleteEvent.
    /// Subsequent Exit Store clicks on the client are silently ignored.</summary>
    public static bool ClientShopChoiceSent;

    /// <summary>Client-side: true once all players have finished shopping and the
    /// host is doing the post-shop navigation shot. The waiting overlay stays up
    /// with a different message until the scene changes.</summary>
    public static bool ShopAwaitingHostNavigation;

    // --- Treasure wait-for-all ---

    /// <summary>True when the treasure relic selection phase is active.</summary>
    public static bool TreasurePhaseActive;

    /// <summary>True when the host has picked/skipped their treasure relic.</summary>
    public static bool HostTreasureDone;

    /// <summary>Slot indices of clients who have sent their TreasureCompleteEvent.</summary>
    public static System.Collections.Generic.HashSet<int> ClientTreasureChoicesReceived = new System.Collections.Generic.HashSet<int>();

    /// <summary>Number of non-host players expected to finish treasure selection.</summary>
    public static int TotalTreasureClientsExpected;

    /// <summary>True when all clients have finished treasure selection.</summary>
    public static bool AllClientTreasureChoicesReceived => TotalTreasureClientsExpected > 0
        && ClientTreasureChoicesReceived.Count >= TotalTreasureClientsExpected;

    /// <summary>Stored ChestScenarioController so host can resume navigation after all clients finish.</summary>
    public static global::Scenarios.ChestScenarioController PendingChestController;

    /// <summary>True on client when the client has already sent their treasure completion event.</summary>
    public static bool ClientTreasureChoiceSent;

    /// <summary>Client-side: true once all players have finished the treasure relic
    /// selection and the host is still inside the Treasure scene doing the post-selection
    /// navigation shot. The waiting overlay stays up with a "Waiting for host..." message
    /// until the scene actually changes away.</summary>
    public static bool TreasureAwaitingHostNavigation;

    // --- PegMinigame wait-for-all ---

    /// <summary>True when the PegMinigame phase is active (all players playing independently).</summary>
    public static bool PegMinigamePhaseActive;

    /// <summary>True when the host has finished the PegMinigame (chose Add/Skip and is ready to navigate).</summary>
    public static bool HostPegMinigameDone;

    /// <summary>Slot indices of clients who have sent their PegMinigameCompleteEvent.</summary>
    public static System.Collections.Generic.HashSet<int> ClientPegMinigameChoicesReceived = new System.Collections.Generic.HashSet<int>();

    /// <summary>Number of non-host players expected to finish the PegMinigame.</summary>
    public static int TotalPegMinigameClientsExpected;

    /// <summary>True when all clients have finished the PegMinigame.</summary>
    public static bool AllClientPegMinigameChoicesReceived => TotalPegMinigameClientsExpected > 0
        && ClientPegMinigameChoicesReceived.Count >= TotalPegMinigameClientsExpected;

    /// <summary>Stored PegMinigameManager reference so host can resume navigation after all clients finish.</summary>
    public static Peglin.PegMinigame.PegMinigameManager PendingPegMinigameManager;

    /// <summary>True on client when the client has already sent their PegMinigame completion event.</summary>
    public static bool ClientPegMinigameChoiceSent;

    /// <summary>Client-side: true once all players have finished picking their PegMinigame
    /// reward and the host is doing the second (navigation) shot to pick the next stage.
    /// The waiting overlay stays up with a "Waiting for host to select the next stage..."
    /// message until the scene actually changes away.</summary>
    public static bool PegMinigameAwaitingHostNavigation;

    // --- TextScenario wait-for-all ---

    /// <summary>True when the TextScenario dialogue phase is active (all players making choices).</summary>
    public static bool TextScenarioPhaseActive;

    /// <summary>True when the host has finished its own TextScenario dialogue.</summary>
    public static bool HostTextScenarioDone;

    /// <summary>Slot indices of clients who have sent their TextScenarioCompleteEvent.</summary>
    public static System.Collections.Generic.HashSet<int> ClientTextScenarioChoicesReceived = new System.Collections.Generic.HashSet<int>();

    /// <summary>Number of non-host players expected to finish the TextScenario.</summary>
    public static int TotalTextScenarioClientsExpected;

    /// <summary>True when all clients have finished the TextScenario.</summary>
    public static bool AllClientTextScenarioChoicesReceived => TotalTextScenarioClientsExpected > 0
        && ClientTextScenarioChoicesReceived.Count >= TotalTextScenarioClientsExpected;

    /// <summary>Stored DialogueSystemScenario reference so host can resume navigation after all clients finish.</summary>
    public static object PendingDialogueSystemScenario;

    /// <summary>True on client when the client has already sent their TextScenario completion event.</summary>
    public static bool ClientTextScenarioChoiceSent;

    /// <summary>Client-side: true once all players have finished the TextScenario and the
    /// host is doing the post-event navigation shot. The waiting overlay stays up with a
    /// "Waiting for host..." message until the scene actually changes away.</summary>
    public static bool TextScenarioAwaitingHostNavigation;

    // --- Post-battle relic choices (boss/rare) ---

    /// <summary>Host-provided relic choices for the post-battle boss/rare relic selection on the client.</summary>
    public static List<RelicChoiceEntry> PendingPostBattleRelicChoices;

    /// <summary>
    /// Clear local/derived coop-reward flags, but preserve freshly-received
    /// network-driven pending choices (PendingRelicChoices, PendingRewardChoices,
    /// PendingPostBattleRelicChoices). Those are controlled by host events and
    /// must not be clobbered by a local state-transition (e.g., a client's own
    /// GameInit.Start Postfix racing with an already-received RelicChoicesEvent).
    ///
    /// They are already cleaned up through the network lifecycle:
    /// - overwritten by the next host event for the same phase,
    /// - set to null when the user picks (OnRelicChosen / OnRewardChosen),
    /// - implicitly cleared when AllChoicesCompleteEvent arrives.
    /// </summary>
    public static void Reset()
    {
        WaitingForOtherPlayers = false;
        AllChoicesComplete = false;
        HostRelicSelectionActive = false;
        HostHasChosenRelic = false;
        ClientRelicChoicesReceived.Clear();
        TotalClientsExpected = 0;
        PendingGameInitInstance = null;
        PendingSentRewardChoices.Clear();
        ClientRewardChoicesReceived.Clear();
        TotalRewardClientsExpected = 0;
        PerSlotOrbChoices.Clear();
        PerSlotTreasureRelics.Clear();
        ClientInNativeRewardPhase = false;
        HostRewardPhaseActive = false;
        HostRewardsDone = false;
        PendingPostBattleController = null;
        ShopPhaseActive = false;
        HostShopDone = false;
        ClientShopChoicesReceived.Clear();
        TotalShopClientsExpected = 0;
        PendingShopManager = null;
        ShopCompletionProceeded = false;
        ClientShopChoiceSent = false;
        ShopAwaitingHostNavigation = false;
        TreasurePhaseActive = false;
        HostTreasureDone = false;
        ClientTreasureChoicesReceived.Clear();
        TotalTreasureClientsExpected = 0;
        PendingChestController = null;
        ClientTreasureChoiceSent = false;
        TreasureAwaitingHostNavigation = false;
        PegMinigamePhaseActive = false;
        HostPegMinigameDone = false;
        ClientPegMinigameChoicesReceived.Clear();
        TotalPegMinigameClientsExpected = 0;
        PendingPegMinigameManager = null;
        ClientPegMinigameChoiceSent = false;
        PegMinigameAwaitingHostNavigation = false;
        TextScenarioPhaseActive = false;
        HostTextScenarioDone = false;
        ClientTextScenarioChoicesReceived.Clear();
        TotalTextScenarioClientsExpected = 0;
        PendingDialogueSystemScenario = null;
        ClientTextScenarioChoiceSent = false;
        TextScenarioAwaitingHostNavigation = false;
    }
}
