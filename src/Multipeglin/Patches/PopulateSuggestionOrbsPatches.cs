using HarmonyLib;
using Map;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class PopulateSuggestionOrbsPatches
{
    /// <summary>
    /// COOP prefix on PopulateSuggestionOrbs.GenerateAddableOrbs: when the host
    /// has rolled per-slot orb-reward lists, populate the buttons from this
    /// player's list instead of running the native (seeded) roll. Without this,
    /// every player sees identical post-battle orb suggestions because the
    /// seededBattleData.postBattleOrbs list is shared across all instances.
    ///
    /// Falls through to the native logic if no host-broadcast list is available
    /// (e.g. solo play or the event hasn't arrived yet).
    /// </summary>
    [HarmonyPatch(typeof(PopulateSuggestionOrbs), "GenerateAddableOrbs")]
    [HarmonyPrefix]
    public static bool PopulateSuggestionOrbs_GenerateAddableOrbs_CoopPerSlot_Prefix(
        PopulateSuggestionOrbs __instance,
        SeededBattleNodeData seededBattleData)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                return true;
            }

            var mySlot = Events.Handlers.Coop.CoopSlotHelper.GetLocalSlotIndex(services);
            if (mySlot < 0)
            {
                return true;
            }

            if (!Events.Handlers.Coop.CoopRewardState.PerSlotOrbChoices.TryGetValue(mySlot, out var nameList)
                || nameList == null || nameList.Count == 0)
            {
                return true;
            }

            var addOrbButtons = AccessTools.Field(typeof(PopulateSuggestionOrbs), "addOrbButtons")
                ?.GetValue(__instance) as PeglinUI.PostBattle.UpgradeOption[];
            var potentialDownButtons = AccessTools.Field(typeof(PopulateSuggestionOrbs), "potentialDownButtons")
                ?.GetValue(__instance) as UnityEngine.UI.Button[];
            var continueButton = AccessTools.Field(typeof(PopulateSuggestionOrbs), "continueButton")
                ?.GetValue(__instance) as UnityEngine.UI.Button;
            var deckMgr = __instance.deckManager;
            var relicMgr = __instance.relicManager;
            var cruciballMgr = __instance.cruciballManager;
            if (addOrbButtons == null || addOrbButtons.Length == 0 || deckMgr == null)
            {
                return true;
            }

            var prefabs = ResolveOrbPrefabsByName(nameList, deckMgr);
            if (prefabs.Count == 0)
            {
                MultiplayerPlugin.Logger?.LogWarning(
                    $"[CoopOrbReward] Could not resolve any orb prefabs from host list for slot {mySlot} ({string.Join(",", nameList)})");
                return true;
            }

            var num = System.Math.Min(prefabs.Count, addOrbButtons.Length);

            // Replicate the native button visibility + navigation wiring.
            UnityEngine.UI.Button selectOnDown = null;
            if (potentialDownButtons != null)
            {
                foreach (var b in potentialDownButtons)
                {
                    if (b != null && b.gameObject.activeInHierarchy)
                    {
                        selectOnDown = b;
                        break;
                    }
                }
            }

            for (var j = 0; j < addOrbButtons.Length; j++)
            {
                var active = j < num;
                addOrbButtons[j].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                var btn = addOrbButtons[j].GetComponent<UnityEngine.UI.Button>();
                if (btn == null)
                {
                    continue;
                }

                var nav = btn.navigation;
                if (j > 0)
                {
                    nav.selectOnLeft = addOrbButtons[j - 1].GetComponent<UnityEngine.UI.Button>();
                }

                if (j + 1 < num && j < addOrbButtons.Length - 1)
                {
                    nav.selectOnRight = addOrbButtons[j + 1].GetComponent<UnityEngine.UI.Button>();
                }

                nav.selectOnDown = selectOnDown;
                nav.selectOnUp = continueButton;
                btn.navigation = nav;
            }

            for (var m = 0; m < num; m++)
            {
                var prefab = prefabs[m];
                var attack = prefab.GetComponent<Battle.Attacks.Attack>();
                attack?.SoftInit(deckMgr, relicMgr, cruciballMgr);

                var opt = addOrbButtons[m];
                opt.SpecifiedOrb = prefab;
                opt.upgradeType = PeglinUI.PostBattle.UpgradeOption.UpgradeType.INSPECT_NEW_ORB;
            }

            // Selection focus — match native behavior best-effort.
            try
            {
                var resField = AccessTools.Field(typeof(PopulateSuggestionOrbs), "_rewiredEventSystem");
                var res = resField?.GetValue(__instance);
                if (res != null)
                {
                    var hookup = (res as UnityEngine.MonoBehaviour)
                        ?.GetComponent<PeglinUI.ControllerSupport.ControllerMenuHookup>();
                    hookup?.LastSelected = addOrbButtons[0].gameObject;
                }
            }
            catch { /* selection focus is non-critical */ }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[CoopOrbReward] Populated {num} per-slot orbs for slot {mySlot}: {string.Join(",", nameList)}");
            return false;
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning(
                $"[CoopOrbReward] GenerateAddableOrbs override failed, falling through: {ex.Message}");
            return true;
        }
    }
}
