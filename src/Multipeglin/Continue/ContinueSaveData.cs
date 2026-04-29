using System;
using System.Collections.Generic;
using Multipeglin.GameState;

namespace Multipeglin.Continue;

/// <summary>
/// Persistent format for a co-op continue save. Bumped whenever the on-disk
/// schema changes in a way the loader can't tolerate. The Continue UI will
/// hide saves whose SchemaVersion doesn't match SCHEMA_VERSION_CURRENT.
/// </summary>
public sealed class ContinueSaveData
{
    public const int SCHEMA_VERSION_CURRENT = 1;

    public int SchemaVersion { get; set; } = SCHEMA_VERSION_CURRENT;

    /// <summary>The mod's MultiplayerPluginInfo.VERSION at save time.</summary>
    public string ModVersion { get; set; }

    /// <summary>UnityEngine.Application.version at save time.</summary>
    public string GameVersion { get; set; }

    /// <summary>UTC ISO timestamp of the most recent save.</summary>
    public string SavedAtUtc { get; set; }

    /// <summary>StaticGameData.currentSeed.</summary>
    public string Seed { get; set; }

    /// <summary>CruciballManager.currentCruciballLevel.</summary>
    public int CruciballLevel { get; set; }

    /// <summary>MapController.Act (1-based).</summary>
    public int Act { get; set; }

    /// <summary>MapController.floorCount (private field).</summary>
    public int FloorCount { get; set; }

    /// <summary>MapController.thisScene as integer (PeglinSceneLoader.Scene).</summary>
    public int MapScene { get; set; }

    /// <summary>Pre-formatted human-readable label, e.g. "Core-3 Cruciball-20".</summary>
    public string StageLabel { get; set; }

    /// <summary>One entry per player slot (slot 0 is the host).</summary>
    public List<ContinuePlayer> Players { get; set; } = new List<ContinuePlayer>();

    /// <summary>Saved TurnManager state (round number etc.). Cleared between battles.</summary>
    public ContinueTurnState TurnState { get; set; } = new ContinueTurnState();

    /// <summary>
    /// Base64-encoded contents of the game's RUN save file (Save_Nr.data) at
    /// the moment of save. Restored by the loader so the game's own Continue
    /// flow can rehydrate seed, RNG state, map nodes, and per-room state.
    /// </summary>
    public string GameRunSaveBase64 { get; set; }

    /// <summary>
    /// JSON-serialized <c>UnityEngine.Random.state</c> captured right after the
    /// post-stage map load, before any continue-side coop restoration runs.
    /// The loader restores this as the final step of ApplyPendingCoopState so
    /// the next battle's pegboard / enemy spawn / shuffles consume RNG from
    /// the exact state the original session would have. Without this, the
    /// continue path's <c>TurnManager.BuildTurnOrder</c> + per-slot deck
    /// shuffles in <c>LoadPlayerState</c> consume RNG that the original run
    /// did not, so the next battle's layout drifts even with the same seed.
    /// </summary>
    public string RandomStateJson { get; set; }

    public DateTime ParsedSavedAt
    {
        get
        {
            if (DateTime.TryParse(SavedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                return dt;
            }

            return DateTime.MinValue;
        }
    }
}

public sealed class ContinuePlayer
{
    public int SlotIndex { get; set; }

    public string PlayerName { get; set; }

    public int ChosenClass { get; set; }

    /// <summary>Full per-player coop state: deck, relics, health, gold, status effects.</summary>
    public CoopPlayerState State { get; set; }
}

public sealed class ContinueTurnState
{
    public List<int> TurnOrder { get; set; } = new List<int>();

    public int RoundNumber { get; set; }
}
