using PeglinMods.Multiplayer.Events.Network.Coop;

namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

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

    public static void Reset()
    {
        PendingRelicChoices = null;
        PendingRewardChoices = null;
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
        ClientInNativeRewardPhase = false;
        HostRewardPhaseActive = false;
        HostRewardsDone = false;
        PendingPostBattleController = null;
    }
}
