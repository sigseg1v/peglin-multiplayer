using Battle;
using HarmonyLib;
using Multipeglin.Multiplayer;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class PlayerHealthControllerPatches
{
    [HarmonyPatch(typeof(PlayerHealthController), "Damage")]
    [HarmonyPrefix]
    public static bool PlayerHealthController_Damage_Prefix()
    {
        if (!ShouldSuppressClientLogic)
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

        MultiplayerPlugin.Logger?.LogInfo("[ClientPatch] Blocked PlayerHealthController.Damage on client");
        return false;
    }

    /// <summary>
    /// In coop, prevent the game from triggering game over when the active player dies
    /// unless ALL coop players are dead. Dead players just skip turns.
    /// </summary>
    [HarmonyPatch(typeof(PlayerHealthController), "CheckForDeathAndUpdateBar")]
    [HarmonyPrefix]
    public static bool PlayerHealthController_CheckForDeathAndUpdateBar_Prefix(PlayerHealthController __instance)
    {
        if (!IsHosting || !UI.LobbyUI.GameStartReceived)
        {
            return true;
        }

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) != true)
        {
            return true;
        }

        if (coopState.TotalPlayerCount < 2)
        {
            return true;
        }

        // If health is still above 0, let the normal update run (just updates the bar)
        var healthField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_playerHealth");
        var healthVar = healthField?.GetValue(__instance);
        if (healthVar == null)
        {
            return true;
        }

        var valueProp = healthVar.GetType().GetProperty("Value");
        if (valueProp == null)
        {
            return true;
        }

        var hp = (float)valueProp.GetValue(healthVar);

        if (hp > 0f)
        {
            return true; // not dead, let normal flow update the bar
        }

        // Active player is dead. Only allow game over if ALL players are dead.
        if (!coopState.AllPlayersDead)
        {
            // Clamp the FloatVariable to 0 so downstream reads (CoopState save,
            // heartbeat health sync, UI bar) don't see negative HP. Without this,
            // the dead player displays as -5/100 and TurnManager may race with
            // native flow that reads the negative value mid-cleanup.
            try
            {
                var setMethod = healthVar.GetType().GetMethod("Set", new[] { typeof(float) });
                if (setMethod != null && hp < 0f)
                {
                    setMethod.Invoke(healthVar, new object[] { 0f });
                }
            }
            catch
            {
            }

            // Also clamp the active player's stored CoopPlayerState so TurnManager
            // reads exactly 0 (not a negative) when deciding to skip this slot.
            var activeState = coopState.GetPlayerState(coopState.ActivePlayerSlot);
            if (activeState != null && activeState.CurrentHealth < 0f)
            {
                activeState.CurrentHealth = 0f;
            }

            MultiplayerPlugin.Logger?.LogInfo(
                $"[ClientPatches] Active player (slot {coopState.ActivePlayerSlot}) died but other players alive — suppressing game over, clamped hp to 0");
            return false; // block CheckForDeathAndUpdateBar entirely
        }

        // All players dead — allow normal game over
        return true;
    }

    /// <summary>
    /// In coop, <c>PlayerHealthController</c> is a single singleton that the host
    /// hot-swaps between slots as turns change. When a non-host player (e.g. client
    /// slot 1) fires Restorb, the host's native <c>Heal()</c> spawns a floating
    /// "+N" popup and heal particle effect at the PHC's own transform — which is
    /// the host's (slot 0) visual position. Visually the heal looks like it hit
    /// the wrong player.
    ///
    /// Suppress the native VFX fields when the active slot isn't the local host
    /// slot (0). The <c>CoopPlayerVisuals</c> HP text still updates via heartbeat
    /// so the actual healed slot shows the new HP value. We restore the fields
    /// in the postfix so normal single-player / host-turn heals still render.
    /// </summary>
    [HarmonyPatch(typeof(PlayerHealthController), "Heal")]
    [HarmonyPrefix]
    public static void PlayerHealthController_Heal_Prefix(PlayerHealthController __instance, ref object[] __state)
    {
        __state = null;
        if (!IsHosting)
        {
            return;
        }

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<GameState.CoopStateManager>(out var coopState) != true)
        {
            return;
        }

        if (coopState.TotalPlayerCount < 2)
        {
            return;
        }

        if (coopState.ActivePlayerSlot == 0)
        {
            return; // heal is for the host itself — let VFX play
        }

        try
        {
            var floatingTextField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_damageFloatingTextPrefab");
            var healSfxField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_healSFX");
            var particleField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "HealParticleAnim");

            __state = new object[]
            {
                floatingTextField, floatingTextField?.GetValue(__instance),
                healSfxField, healSfxField?.GetValue(__instance),
                particleField, particleField?.GetValue(__instance),
            };

            floatingTextField?.SetValue(__instance, null);
            healSfxField?.SetValue(__instance, null);
            particleField?.SetValue(__instance, null);
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Heal VFX suppression failed: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(PlayerHealthController), "Heal")]
    [HarmonyPostfix]
    public static void PlayerHealthController_Heal_Postfix(PlayerHealthController __instance, object[] __state)
    {
        if (__state == null)
        {
            return;
        }

        try
        {
            var floatingTextField = __state[0] as System.Reflection.FieldInfo;
            var floatingTextVal = __state[1];
            var healSfxField = __state[2] as System.Reflection.FieldInfo;
            var healSfxVal = __state[3];
            var particleField = __state[4] as System.Reflection.FieldInfo;
            var particleVal = __state[5];

            floatingTextField?.SetValue(__instance, floatingTextVal);
            healSfxField?.SetValue(__instance, healSfxVal);
            particleField?.SetValue(__instance, particleVal);
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatches] Heal VFX restore failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Hide the native HP bar + health text when in a multiplayer session (host or
    /// client). The per-player HP bars rendered under each sprite replace it, and
    /// the freed canvas slot is used for the Skip Turn button.
    /// </summary>
    [HarmonyPatch(typeof(PlayerHealthController), "OnEnable")]
    [HarmonyPostfix]
    public static void PlayerHealthController_OnEnable_Postfix(PlayerHealthController __instance)
    {
        try
        {
            if (MultiplayerPlugin.Services == null)
            {
                return;
            }

            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode))
            {
                return;
            }

            if (!mode.IsHosting && !mode.IsSpectating)
            {
                return;
            }

            var healthTextField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_healthText");
            if (healthTextField?.GetValue(__instance) is UnityEngine.Component healthText && healthText != null)
            {
                healthText.gameObject.SetActive(false);
            }

            var barField = HarmonyLib.AccessTools.Field(typeof(PlayerHealthController), "_barScript");
            if (barField?.GetValue(__instance) is UnityEngine.Component barScript && barScript != null)
            {
                barScript.gameObject.SetActive(false);
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[ClientPatch] Hide HP bar failed: {ex.Message}");
        }
    }
}
