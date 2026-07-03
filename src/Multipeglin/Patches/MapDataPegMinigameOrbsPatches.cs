using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class MapDataPegMinigameOrbsPatches
{
    [HarmonyPatch(typeof(Peglin.PegMinigame.MapDataPegMinigameOrbs), "PopulateRewards")]
    [HarmonyPrefix]
    public static bool MapDataPegMinigameOrbs_PopulateRewards_PerSlot_Prefix(
        Peglin.PegMinigame.MapDataPegMinigameOrbs __instance)
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (!AllowPegMinigameLogic)
        {
            return true;
        }

        try
        {
            var mySlot = Events.Handlers.Coop.CoopSlotHelper.GetLocalSlotIndex(MultiplayerPlugin.Services);
            if (mySlot < 0)
            {
                return true;
            }

            var deckMgr = __instance.deckManager;
            if (deckMgr == null)
            {
                return true;
            }

            var pool = deckMgr.GetRandomOrbPool();
            if (pool == null || pool.Count == 0)
            {
                // Orb pools weren't set up for this class (the native class-select
                // UI is skipped in multiplayer). Falling through to native here
                // would throw — Random.Range over an empty pool — and abort
                // PegMinigameManager.Initialize before the ball is created,
                // softlocking the run. Populate the pools now and retry rather
                // than hand control back to the crashing native path.
                deckMgr.SetupClassOrbPools(StaticGameData.chosenClass);
                pool = deckMgr.GetRandomOrbPool();
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[ClientPatch] PegMinigame orb pool was empty — ran SetupClassOrbPools({StaticGameData.chosenClass}), now {pool?.Count ?? 0} orbs");
                if (pool == null || pool.Count == 0)
                {
                    return true;
                }
            }

            // Stable order so the slot-keyed RNG produces consistent picks.
            var sorted = new System.Collections.Generic.List<UnityEngine.GameObject>(pool);
            sorted.Sort((a, b) => string.CompareOrdinal(a?.name, b?.name));

            var count = System.Math.Max(1, __instance.numberOfRewards);
            var take = System.Math.Min(count, sorted.Count);
            var seed = unchecked((StaticGameData.currentSeed ?? string.Empty).GetHashCode()
                ^ (mySlot * 7919)
                ^ (StaticGameData.totalFloorCount * 104729)
                ^ 0x4F2BD17);
            var rng = new System.Random(seed);

            var rewards = new System.Collections.Generic.List<Peglin.PegMinigame.Reward>();
            var picked = new System.Collections.Generic.List<string>();
            for (var i = 0; i < take; i++)
            {
                var j = i + rng.Next(0, sorted.Count - i);
                var tmp = sorted[i];
                sorted[i] = sorted[j];
                sorted[j] = tmp;
                var orb = sorted[i];
                if (orb == null)
                {
                    continue;
                }

                // Replicate native Act-based upgrade logic.
                var chosen = orb;
                var act = Map.MapController.instance != null ? Map.MapController.instance.Act : 0;
                if (act > 1 && chosen.TryGetComponent<Battle.Attacks.Attack>(out var c1) && c1.NextLevelPrefab != null)
                {
                    chosen = c1.NextLevelPrefab;
                    if (act > 2 && chosen.TryGetComponent<Battle.Attacks.Attack>(out var c2) && c2.NextLevelPrefab != null)
                    {
                        chosen = c2.NextLevelPrefab;
                    }
                }

                rewards.Add(new Peglin.PegMinigame.OrbReward(chosen, deckMgr));
                picked.Add(chosen.name);
            }

            __instance.Rewards = rewards;

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatch] PegMinigame orb rewards (slot {mySlot}) = [{string.Join(",", picked)}]");
            return false;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] PegMinigameOrbs PopulateRewards override failed: {ex.Message}");
            return true;
        }
    }
}
