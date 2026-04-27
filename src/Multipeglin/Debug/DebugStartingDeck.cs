using System;
using System.Linq;
using Battle.Attacks;
using UnityEngine;

namespace Multipeglin.Debug;

/// <summary>
/// Playtest-only: when MULTIPEGLIN_DEBUG is set, grant the host a copy of the
/// two easter-egg orbs (BigBossD9000-Lvl1, BeastWarb-Lvl1) at run start so they
/// can be exercised without having to roll them as draft rewards.
///
/// Called from GameInit.Start postfix before CoopStateManager captures the
/// host's initial state so the snapshot includes the granted orbs.
/// </summary>
internal static class DebugStartingDeck
{
    private const string EnvVar = "MULTIPEGLIN_DEBUG";

    private static readonly string[] OrbPrefabNames =
    {
        "BigBossD9000-Lvl1",
        "BeastWarb-Lvl1",
    };

    public static void TryGrantHostDebugOrbs()
    {
        var v = Environment.GetEnvironmentVariable(EnvVar);
        var enabled = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        if (!enabled)
        {
            return;
        }

        // Nudge the CustomOrbs assembly to build its prefabs first — postfix order
        // across separate Harmony patches isn't deterministic, so the first run
        // could otherwise race the registry build.
        TryEnsureCustomOrbsBuilt();

        var deckMgrs = Resources.FindObjectsOfTypeAll<DeckManager>();
        if (deckMgrs == null || deckMgrs.Length == 0)
        {
            MultiplayerPlugin.Logger?.LogWarning("[DebugDeck] No DeckManager found — skipping debug orb grant");
            return;
        }

        var dm = deckMgrs[0];

        foreach (var name in OrbPrefabNames)
        {
            var prefab = FindOrbPrefab(name);
            if (prefab == null)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[DebugDeck] Prefab '{name}' not found (CustomOrbs not built yet?) — skipping");
                continue;
            }

            try
            {
                dm.AddOrbToDeck(prefab);
                MultiplayerPlugin.Logger?.LogInfo($"[DebugDeck] Granted '{name}' to host deck");
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[DebugDeck] AddOrbToDeck('{name}') failed: {ex.Message}");
            }
        }
    }

    private static void TryEnsureCustomOrbsBuilt()
    {
        try
        {
            var type = Type.GetType("Multipeglin.CustomOrbs.CustomOrbRegistry, Multipeglin.CustomOrbs");
            var method = type?.GetMethod("EnsureBuilt",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, null);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[DebugDeck] CustomOrbRegistry.EnsureBuilt nudge failed: {ex.Message}");
        }
    }

    private static GameObject FindOrbPrefab(string name)
    {
        return Resources.FindObjectsOfTypeAll<Attack>()
            .Where(a => a != null && a.gameObject != null && a.gameObject.name == name
                && string.IsNullOrEmpty(a.gameObject.scene.name))
            .Select(a => a.gameObject)
            .FirstOrDefault();
    }
}
