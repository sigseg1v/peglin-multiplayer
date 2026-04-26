using System.Collections.Generic;

namespace Multipeglin.Events.Network.Coop;

/// <summary>
/// Host → clients: a condensed snapshot of StaticGameData.CurrentRunStats plus
/// per-player damage tallies, sent when the host enters the RunSummary scene
/// so the client can reconstruct the summary screen.
/// </summary>
public class RunStatsSnapshotEvent
{
    // Scalar fields used by RunSummary.OnEnable / RunStatisticsDetails.Initialize
    public bool HasWon { get; set; }

    public bool HasWonCore { get; set; }

    public bool IsCustomRun { get; set; }

    public bool VampireDealTaken { get; set; }

    public int SelectedClass { get; set; }

    public int CruciballLevel { get; set; }

    public string EndDateIso { get; set; }

    public string Seed { get; set; }

    public long RunTimerElapsedMs { get; set; }

    public int FinalHp { get; set; }

    public int MaxHp { get; set; }

    public int TotalDamageDealt { get; set; }

    public int CoinsEarned { get; set; }

    public int PegsHit { get; set; }

    public int PegsHitRefresh { get; set; }

    public int PegsHitCrit { get; set; }

    public int BombsThrown { get; set; }

    public int BombsThrownRigged { get; set; }

    // Enum-valued collections — send as int arrays
    public List<int> VisitedRooms { get; set; } = new List<int>();

    public List<int> VisitedBosses { get; set; } = new List<int>();

    public List<int> Relics { get; set; } = new List<int>();

    public List<int> Challenges { get; set; } = new List<int>();

    // Dense arrays indexed by enum value
    public List<int> StatusEffectStacks { get; set; } = new List<int>();

    public List<int> SlimePegCounts { get; set; } = new List<int>();

    public List<OrbStatsEntry> Orbs { get; set; } = new List<OrbStatsEntry>();

    public List<EnemyStatsEntry> Enemies { get; set; } = new List<EnemyStatsEntry>();

    // Per-player totals — drives the per-player stat lines on the summary.
    public List<PerPlayerStats> Players { get; set; } = new List<PerPlayerStats>();
}

public class OrbStatsEntry
{
    public string Id { get; set; }

    public string Name { get; set; }

    public int DamageDealt { get; set; }

    public int TimesFired { get; set; }

    public int TimesDiscarded { get; set; }

    public int TimesRemoved { get; set; }

    public bool Starting { get; set; }

    public int AmountInDeck { get; set; }

    public int[] LevelInstances { get; set; } = new int[3];
}

public class EnemyStatsEntry
{
    public string Name { get; set; }

    public int AmountFought { get; set; }

    public int MeleeDamageReceived { get; set; }

    public int RangedDamageReceived { get; set; }

    public bool DefeatedBy { get; set; }
}

public class PerPlayerStats
{
    public int SlotIndex { get; set; }

    public string PlayerName { get; set; }

    public long DamageDealt { get; set; }

    public long DamageTaken { get; set; }

    // Per-player loadout shown on the paginated summary so each client sees
    // their own orbs/relics/class, not the host's singleton values.
    public int ChosenClass { get; set; }

    public int FinalHp { get; set; }

    public int MaxHp { get; set; }

    public int Gold { get; set; }

    public bool IsAlive { get; set; }

    // Relic effect enum values (RelicEffect)
    public List<int> Relics { get; set; } = new List<int>();

    // Orbs held at run end — prefab name + level
    public List<PerPlayerOrb> Orbs { get; set; } = new List<PerPlayerOrb>();
}

public class PerPlayerOrb
{
    public string PrefabName { get; set; }

    public int Level { get; set; }
}
