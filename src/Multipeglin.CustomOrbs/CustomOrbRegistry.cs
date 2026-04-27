using System.Collections.Generic;
using System.Linq;
using Battle.Attacks;
using UnityEngine;

namespace Multipeglin.CustomOrbs;

/// <summary>
/// Clones existing in-game orb prefabs (Worbhammer, Bullyball) into custom
/// easter-egg orbs (Big Boss D9000, Beast Warb), then injects them into every
/// <see cref="OrbPool"/> in the game so they can roll as draft/shop rewards.
///
/// Idempotent: repeat calls are no-ops once the orbs exist.
/// </summary>
internal static class CustomOrbRegistry
{
    private const string D9000_PREFIX = "BigBossD9000";
    private const string BEAST_PREFIX = "BeastWarb";

    private static readonly int[] D9000Chances = { 100, 60, 30 };

    private static GameObject[] _d9000Levels;
    private static GameObject[] _beastLevels;
    private static bool _injectedIntoPools;

    public static bool IsInitialized => _d9000Levels != null && _beastLevels != null;

    public static void EnsureBuilt()
    {
        CustomOrbLocalization.EnsureRegistered();

        if (IsInitialized)
        {
            TryInjectIntoPools();
            return;
        }

        var worbSrc = FindOrbPrefabs("Worbhammer");
        var bullySrc = FindOrbPrefabs("Bullyball");

        if (worbSrc == null || bullySrc == null)
        {
            Plugin.Logger?.LogDebug(
                $"[CustomOrbs] source prefabs not loaded yet (worb={worbSrc?.Length ?? 0}, bully={bullySrc?.Length ?? 0})");
            return;
        }

        _d9000Levels = BuildD9000Levels(worbSrc);
        _beastLevels = BuildBeastLevels(bullySrc);

        Plugin.Logger?.LogInfo(
            $"[CustomOrbs] built custom orbs: D9000 levels={_d9000Levels.Length}, BeastWarb levels={_beastLevels.Length}");

        TryInjectIntoPools();
    }

    private static GameObject[] FindOrbPrefabs(string namePrefix)
    {
        var matches = Resources.FindObjectsOfTypeAll<Attack>()
            .Where(a => a != null && a.gameObject != null
                && a.gameObject.name.StartsWith(namePrefix + "-Lvl", System.StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(a.gameObject.scene.name))
            .GroupBy(a => a.gameObject.name)
            .Select(g => g.First().gameObject)
            .OrderBy(g => g.name)
            .ToArray();

        if (matches.Length < 3)
        {
            return null;
        }

        return matches.Take(3).ToArray();
    }

    private static GameObject[] BuildD9000Levels(GameObject[] sources)
    {
        var levels = new GameObject[3];

        for (var i = 0; i < 3; i++)
        {
            var clone = Object.Instantiate(sources[i]);
            clone.name = $"{D9000_PREFIX}-Lvl{i + 1}";
            clone.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(clone);
            clone.SetActive(false);

            var attack = clone.GetComponent<Attack>();
            if (attack != null)
            {
                attack.DamagePerPeg = 0;
                attack.CritDamagePerPeg = 0;
                attack.Level = i + 1;
                attack.locNameString = "bigbossd9000";
            }

            var beh = clone.AddComponent<BigBossD9000Behaviour>();
            beh.OneInChance = D9000Chances[i];

            levels[i] = clone;
        }

        // Stitch upgrade chain.
        WireUpgradeChain(levels);
        return levels;
    }

    private static GameObject[] BuildBeastLevels(GameObject[] sources)
    {
        var levels = new GameObject[3];
        var dmg = new (int n, int nc, int b, int bc)[]
        {
            (1, 1, 15, 20),
            (1, 1, 20, 25),
            (1, 1, 30, 35),
        };

        for (var i = 0; i < 3; i++)
        {
            var clone = Object.Instantiate(sources[i]);
            clone.name = $"{BEAST_PREFIX}-Lvl{i + 1}";
            clone.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(clone);
            clone.SetActive(false);

            var attack = clone.GetComponent<Attack>();
            if (attack != null)
            {
                attack.DamagePerPeg = dmg[i].n;
                attack.CritDamagePerPeg = dmg[i].nc;
                attack.Level = i + 1;
                attack.locNameString = "beastwarb";
            }

            var beh = clone.AddComponent<BeastWarbBehaviour>();
            beh.NormalDamage = dmg[i].n;
            beh.NormalCritDamage = dmg[i].nc;
            beh.BossDamage = dmg[i].b;
            beh.BossCritDamage = dmg[i].bc;

            levels[i] = clone;
        }

        WireUpgradeChain(levels);
        return levels;
    }

    private static void WireUpgradeChain(GameObject[] levels)
    {
        for (var i = 0; i < levels.Length; i++)
        {
            var attack = levels[i].GetComponent<Attack>();
            if (attack == null)
            {
                continue;
            }

            attack.PreviousLevelPrefab = i > 0 ? levels[i - 1] : null;
            attack.NextLevelPrefab = i < levels.Length - 1 ? levels[i + 1] : null;
        }
    }

    private static void TryInjectIntoPools()
    {
        if (_injectedIntoPools || !IsInitialized)
        {
            return;
        }

        var pools = Resources.FindObjectsOfTypeAll<OrbPool>();
        if (pools == null || pools.Length == 0)
        {
            return;
        }

        var injected = 0;
        var add = new[] { _d9000Levels[0], _beastLevels[0] };

        foreach (var pool in pools)
        {
            if (pool == null || pool.AvailableOrbs == null)
            {
                continue;
            }

            var existing = new HashSet<GameObject>(pool.AvailableOrbs);
            var toAdd = add.Where(a => a != null && !existing.Contains(a)).ToArray();
            if (toAdd.Length == 0)
            {
                continue;
            }

            var combined = new List<GameObject>(pool.AvailableOrbs);
            combined.AddRange(toAdd);
            pool.AvailableOrbs = combined.ToArray();
            injected++;
        }

        if (injected > 0)
        {
            Plugin.Logger?.LogInfo($"[CustomOrbs] injected easter-egg orbs into {injected} OrbPool(s)");
        }

        _injectedIntoPools = injected > 0;
    }
}
