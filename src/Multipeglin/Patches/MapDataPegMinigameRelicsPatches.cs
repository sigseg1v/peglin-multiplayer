using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class MapDataPegMinigameRelicsPatches
{
    // =========================================================================
    // PEG MINIGAME PER-SLOT REWARDS — diversify reward sets across players
    //
    // The native PopulateRewards methods read from seeded data (relic queue or
    // orb pool with UnityEngine.Random) which is identical across all clients
    // because the seed is broadcast. Without this patch every player who plays
    // the PegMinigame sees the same 3 relics or 3 orbs.
    //
    // We replace the rewards list with a slot-keyed deterministic pick so each
    // player gets a different set. Only patches the client side — host plays
    // first with native rolling (slot 0 effectively).
    // =========================================================================

    [HarmonyPatch(typeof(Peglin.PegMinigame.MapDataPegMinigameRelics), "PopulateRewards")]
    [HarmonyPrefix]
    public static bool MapDataPegMinigameRelics_PopulateRewards_PerSlot_Prefix(
        Peglin.PegMinigame.MapDataPegMinigameRelics __instance)
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

            var relicMgr = __instance.relicManager;
            var count = System.Math.Max(1, __instance.numberOfRewards);
            var picks = PickMultipleLocalRelics(__instance.rarity, count, relicMgr, mySlot);
            if (picks.Count == 0)
            {
                return true; // fall through to native rather than show empty
            }

            var rewards = new System.Collections.Generic.List<Peglin.PegMinigame.Reward>();
            foreach (var r in picks)
            {
                rewards.Add(new Peglin.PegMinigame.RelicReward(r, relicMgr, __instance.relicInfoWidget));
            }

            __instance.Rewards = rewards;

            var names = new System.Collections.Generic.List<string>();
            foreach (var r in picks)
            {
                names.Add(r?.name ?? "<null>");
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatch] PegMinigame relic rewards (slot {mySlot}, rarity {__instance.rarity}) = [{string.Join(",", names)}]");
            return false;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] PegMinigameRelics PopulateRewards override failed: {ex.Message}");
            return true;
        }
    }
}
