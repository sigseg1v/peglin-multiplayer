using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class CurrencyManagerPatches
{
    [HarmonyPatch(typeof(Currency.CurrencyManager), "AddGold")]
    [HarmonyPrefix]
    public static bool CurrencyManager_AddGold_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (AllowCurrencySync)
        {
            return true;
        }

        if (AllowNativeRewardLogic)
        {
            return true;
        }

        if (AllowShopLogic)
        {
            return true;
        }

        if (AllowTreasureLogic)
        {
            return true;
        }

        if (AllowTextScenarioLogic)
        {
            return true;
        }

        return false;
    }

    [HarmonyPatch(typeof(Currency.CurrencyManager), "RemoveGold")]
    [HarmonyPrefix]
    public static bool CurrencyManager_RemoveGold_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        if (AllowCurrencySync)
        {
            return true;
        }

        if (AllowNativeRewardLogic)
        {
            return true;
        }

        if (AllowShopLogic)
        {
            return true;
        }

        if (AllowTextScenarioLogic)
        {
            return true;
        }

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked CurrencyManager.RemoveGold on client");
        return false;
    }

    /// <summary>
    /// Post-battle gold deduction sync: when the client spends gold on the native
    /// BattleUpgradeCanvas (heal, max HP, orb upgrade, orb add), notify the host
    /// immediately so CoopPlayerState.Gold is updated before the next heartbeat
    /// (which would otherwise reset the client's local gold to the stale value).
    /// </summary>
    [HarmonyPatch(typeof(Currency.CurrencyManager), "RemoveGold")]
    [HarmonyPostfix]
    public static void CurrencyManager_RemoveGold_Postfix(int amount)
    {
        if (!ShouldSuppressClientLogic)
        {
            return;
        }

        if (!AllowNativeRewardLogic)
        {
            return;
        }

        if (AllowCurrencySync)
        {
            return;
        }

        if (amount <= 0)
        {
            return;
        }

        try
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<Network.IMessageSender>(out var sender) == true)
            {
                // Defer send to the next Update tick so the post-purchase HP change
                // is observable: native reward flows (Heal / AdjustMaxHealth) run on
                // the same frame as RemoveGold, so reading HP here returns the stale
                // pre-heal value. Enqueue on MainThreadDispatcher runs next frame.
                var evt = new Events.Network.Coop.PostBattleGoldSpentEvent { Amount = amount };
                var dispatcher = Multipeglin.Utility.MainThreadDispatcher.Instance;
                if (dispatcher != null)
                {
                    dispatcher.Enqueue(() =>
                    {
                        try
                        {
                            var hc = UnityEngine.Object.FindObjectOfType<Battle.PlayerHealthController>();
                            evt.CurrentHealth = hc != null ? hc.CurrentHealth : -1f;
                            evt.MaxHealth = hc != null ? hc.MaxHealth : 0f;
                            sender.Send(evt);
                            MultiplayerPlugin.Logger?.LogInfo(
                                $"[ClientPatch] Sent PostBattleGoldSpentEvent amount={evt.Amount} hp={evt.CurrentHealth}/{evt.MaxHealth}");
                        }
                        catch (System.Exception ex2)
                        {
                            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] Deferred send failed: {ex2.Message}");
                        }
                    });
                }
                else
                {
                    // Fallback: send immediately, HP fields unset
                    evt.CurrentHealth = -1f;
                    sender.Send(evt);
                }
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] Failed to send PostBattleGoldSpentEvent: {ex.Message}");
        }
    }
}
