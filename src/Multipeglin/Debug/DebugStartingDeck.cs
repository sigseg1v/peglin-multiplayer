using System;
using System.Collections.Generic;
using System.Linq;
using Battle.Attacks;
using HarmonyLib;
using UnityEngine;

namespace Multipeglin.Debug;

/// <summary>
/// Playtest-only: when MULTIPEGLIN_DEBUG is set, grant the host a copy of the
/// two easter-egg orbs (BigBossD9000-Lvl1, BeastWarb-Lvl1) at run start so they
/// can be exercised without having to roll them as draft rewards.
///
/// Called from GameInit.Start postfix; if CustomOrbs source prefabs aren't
/// loaded yet (PostMainMenu has no Worbhammer/Bullyball in memory), the grant
/// is deferred and retried at MapController.Awake on the host so the orbs land
/// in completeDeck before the host's first battle. CoopSubscriptions captures
/// slot 0 from the live singletons each battle init, so a deferred grant still
/// flows into the host's CoopPlayerState.
/// </summary>
[HarmonyPatch]
internal static class DebugStartingDeck
{
    private const string EnvVar = "MULTIPEGLIN_DEBUG";

    private static readonly string[] OrbPrefabNames =
    {
        "BigBossD9000-Lvl1",
        "BeastWarb-Lvl1",
    };

    private static readonly HashSet<string> _grantedThisRun = new HashSet<string>();
    private static bool _runStarted;

    public static void TryGrantHostDebugOrbs()
    {
        if (!IsEnabled())
        {
            return;
        }

        // Mark a fresh attempt window: GameInit.Start is the run boundary, so
        // any deferred grants from a prior run must be re-attempted.
        if (!_runStarted)
        {
            _grantedThisRun.Clear();
            _runStarted = true;
        }

        TryGrantPending(reason: "GameInit");
    }

    [HarmonyPatch(typeof(Map.MapController), "Awake")]
    [HarmonyPostfix]
    public static void MapController_Awake_Postfix()
    {
        TryRetry("MapController");
    }

    [HarmonyPatch(typeof(Battle.BattleController), "Awake")]
    [HarmonyPostfix]
    public static void BattleController_Awake_Postfix()
    {
        // BattleController.Awake is the latest reliable hook before the host
        // takes its first shot — orb prefabs are guaranteed loaded by now.
        TryRetry("BattleController");
    }

    private static void TryRetry(string reason)
    {
        if (!IsEnabled() || !_runStarted)
        {
            return;
        }

        if (_grantedThisRun.Count == OrbPrefabNames.Length)
        {
            return;
        }

        // Host-only: client decks are mirrored from the host snapshot.
        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<Multiplayer.IMultiplayerMode>(out var mode) != true || !mode.IsHosting)
        {
            return;
        }

        TryGrantPending(reason);
    }

    private static void TryGrantPending(string reason)
    {
        // Nudge the CustomOrbs assembly to build its prefabs first — postfix order
        // across separate Harmony patches isn't deterministic, so the first run
        // could otherwise race the registry build.
        TryEnsureCustomOrbsBuilt();

        var deckMgrs = Resources.FindObjectsOfTypeAll<DeckManager>();
        if (deckMgrs == null || deckMgrs.Length == 0)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[DebugDeck/{reason}] No DeckManager found — deferring debug orb grant");
            return;
        }

        var dm = deckMgrs[0];

        foreach (var name in OrbPrefabNames)
        {
            if (_grantedThisRun.Contains(name))
            {
                continue;
            }

            var prefab = FindOrbPrefab(name);
            if (prefab == null)
            {
                MultiplayerPlugin.Logger?.LogInfo(
                    $"[DebugDeck/{reason}] Prefab '{name}' not yet available — will retry");
                continue;
            }

            try
            {
                dm.AddOrbToDeck(prefab);
                _grantedThisRun.Add(name);
                MultiplayerPlugin.Logger?.LogInfo($"[DebugDeck/{reason}] Granted '{name}' to host deck");
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[DebugDeck/{reason}] AddOrbToDeck('{name}') failed: {ex.Message}");
            }
        }

        if (_grantedThisRun.Count == OrbPrefabNames.Length)
        {
            // After both grants land, refresh the host's CoopPlayerState snapshot so
            // a deferred grant (post-CaptureInitialState) still ends up in slot 0's
            // saved deck — otherwise the swap-in next turn would overwrite the live
            // deck with a stale copy that omits the new orbs.
            try
            {
                var services = MultiplayerPlugin.Services;
                if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) == true && coopState != null)
                {
                    coopState.SaveActivePlayerState();
                }
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[DebugDeck/{reason}] post-grant SaveActivePlayerState failed: {ex.Message}");
            }
        }
    }

    private static bool IsEnabled()
    {
        var v = Environment.GetEnvironmentVariable(EnvVar);
        return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
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
        catch (System.Reflection.TargetInvocationException tie)
        {
            // Unwrap so the underlying EnsureBuilt failure is actually visible.
            var inner = tie.InnerException ?? tie;
            MultiplayerPlugin.Logger?.LogWarning($"[DebugDeck] CustomOrbRegistry.EnsureBuilt nudge failed: {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[DebugDeck] CustomOrbRegistry.EnsureBuilt nudge failed: {ex}");
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
