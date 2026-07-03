using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Scenarios;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Scenarios;

/// <summary>
/// Coordinates the "detonate all navigation bombs -> secret bonus chest room"
/// easter egg across the lobby. The native flow transitions only the player who
/// detonated the last bomb (it bypasses the parallel-shoot vote system entirely),
/// desyncing and softlocking everyone else in the navigation screen.
///
/// Instead: each peer counts its own local bomb detonations (real + ghost balls).
/// Whoever hits zero first reports it — the host dedupes, applies the transition
/// locally, and rebroadcasts BonusChestTriggeredEvent so every player abandons
/// the navigate phase and loads the bonus TREASURE room together. The existing
/// treasure-phase machinery (per-slot relic broadcast, wait-for-all) then shows
/// the bonus relic to all players.
/// </summary>
public static class BonusChestSync
{
    /// <summary>
    /// True once this machine has started the bonus-chest transition for the
    /// current navigation. Dedupes double triggers (local counter + host
    /// rebroadcast, or two players detonating near-simultaneously). Reset at
    /// every CoopNavigateState.StartPhase.
    /// </summary>
    public static bool TransitionStarted;

    public static void Reset()
    {
        TransitionStarted = false;
    }

    /// <summary>
    /// Called from the Harmony prefixes when this machine's local bomb counter
    /// reaches zero. Host: trigger immediately. Client: report to host and wait
    /// for the rebroadcast (dumb canvas — the host orchestrates the transition).
    /// </summary>
    public static void LocalPlayerTriggered(string source)
    {
        if (TransitionStarted)
        {
            return;
        }

        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return;
        }

        if (services.TryResolve<IMultiplayerMode>(out var mode) && mode.IsHosting)
        {
            if (services.TryResolve<IGameEventRegistry>(out var reg))
            {
                MultiplayerPlugin.Logger?.LogInfo($"[BonusChest] Host detonated all bombs (source={source}) — broadcasting transition");
                // ServerHandler applies locally and rebroadcasts to all clients.
                reg.Dispatch(new BonusChestTriggeredEvent { Source = source });
            }

            return;
        }

        if (services.TryResolve<Multipeglin.Network.IMessageSender>(out var sender))
        {
            MultiplayerPlugin.Logger?.LogInfo($"[BonusChest] Client detonated all bombs (source={source}) — reporting to host");
            sender.Send(new BonusChestTriggeredEvent { Source = source });
        }
    }

    /// <summary>
    /// Performs the bonus-chest transition on this machine: tears down the
    /// navigate phase, marks the bonus chest statics, kills all live nav balls,
    /// and fades into the TREASURE scene. Mirrors the native HandlePegDestruction
    /// transition but runs on every peer instead of just the detonator.
    /// </summary>
    public static void ApplyLocally(string source, bool isHost)
    {
        if (TransitionStarted)
        {
            return;
        }

        TransitionStarted = true;
        var log = MultiplayerPlugin.Logger;
        log?.LogInfo($"[BonusChest] Applying bonus chest transition (source={source}, isHost={isHost})");

        // Abandon the in-flight parallel-shoot navigate phase. Without this the
        // stale phase blocks PachinkoBall.Fire in the bonus room (the
        // LocalVoteCast/Resolved guard) and the host's vote watchdog keeps
        // waiting on votes that will never come.
        CoopNavigateState.Reset();

        // The nav phase set AllChoicesComplete=true; the bonus treasure room is
        // a fresh choice phase. Without clearing it, ChestScenarioController.Skip
        // on the client silently no-ops and never sends TreasureCompleteEvent.
        CoopRewardState.AllChoicesComplete = false;
        CoopRewardState.WaitingForOtherPlayers = false;

        if (source == "store_robbed")
        {
            ApplyStoreRobbedStatics(log);
        }
        else
        {
            ApplyChestStatics(log);
        }

        // Native flow disables every live pachinko ball before fading.
        foreach (var pb in Object.FindObjectsOfType<PachinkoBall>())
        {
            pb.gameObject.SetActive(false);
        }

        var noc = FindActiveNavController();
        if (noc == null)
        {
            log?.LogError("[BonusChest] No NavOnlyController found — cannot transition to bonus chest room");
            return;
        }

        if (!isHost)
        {
            // Client scene loads are blocked unless sync-initiated; gate this one
            // through, and ignore stale map syncs until the host confirms Treasure.
            GameState.Appliers.MapStateApplier.AwaitingHostSceneConfirmation = "Treasure";
            Patches.MultiplayerClientPatches.AllowNextSceneLoad = true;
        }

        noc.FadeAndLoad(global::Loading.PeglinSceneLoader.Scene.TREASURE);
    }

    /// <summary>Treasure-room chest navigation: mirror ChestScenarioController.HandlePegDestruction.</summary>
    private static void ApplyChestStatics(BepInEx.Logging.ManualLogSource log)
    {
        var chest = Object.FindObjectOfType<global::Scenarios.ChestScenarioController>();
        if (chest != null)
        {
            // Plays the bonus sound, increments bombChestsOpened, sets isBonusChest.
            chest.DoBonusChestStuff();
        }
        else
        {
            log?.LogWarning("[BonusChest] No ChestScenarioController found — setting statics directly");
            global::Scenarios.ChestScenarioController.bombChestsOpened++;
            global::Scenarios.ChestScenarioController.isBonusChest = true;
        }
    }

    /// <summary>Scenario pegboard bombs: mirror ScenarioNavigationBonusChest.HandlePegDestruction.</summary>
    private static void ApplyStoreRobbedStatics(BepInEx.Logging.ManualLogSource log)
    {
        global::RNG.Scenarios.ScenarioNavigationBonusChest.StoreRobbed = true;
        global::Scenarios.ChestScenarioController.isBonusChest = true;
        global::Scenarios.ChestScenarioController.rareChestRequested = true;

        // Best-effort: play the native bonus chest sting.
        try
        {
            var bonus = Object.FindObjectOfType<global::RNG.Scenarios.ScenarioNavigationBonusChest>();
            if (bonus != null && bonus._bonusChestSound != null)
            {
                bonus.GetComponent<AudioSource>()?.PlayOneShot(bonus._bonusChestSound);
            }
        }
        catch
        {
        }

        // Native: GameObject.FindWithTag("MapController").GetComponent<MapController>()
        //   .AssignTreasureDataToStatic() — copies _treasureMapData into
        // StaticGameData.dataToLoad so the TREASURE scene's chest controller has
        // doodads/background. Deterministic field copy, safe on clients too.
        try
        {
            var mc = global::Map.MapController.instance
                ?? GameObject.FindWithTag("MapController")?.GetComponent<global::Map.MapController>();
            if (mc != null)
            {
                mc.AssignTreasureDataToStatic();
            }
            else
            {
                log?.LogWarning("[BonusChest] No MapController found — StaticGameData.dataToLoad not refreshed");
            }
        }
        catch (System.Exception ex)
        {
            log?.LogWarning($"[BonusChest] AssignTreasureDataToStatic failed: {ex.Message}");
        }
    }

    private static global::NavOnlyController FindActiveNavController()
    {
        var nocs = Resources.FindObjectsOfTypeAll<global::NavOnlyController>();
        global::NavOnlyController fallback = null;
        foreach (var n in nocs)
        {
            if (n == null || n.gameObject == null || !n.gameObject.scene.IsValid())
            {
                continue;
            }

            if (n.gameObject.activeInHierarchy)
            {
                return n;
            }

            fallback ??= n;
        }

        return fallback;
    }
}
