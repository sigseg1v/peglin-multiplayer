using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class Act4BossPegBoardFrameManagerPatches
{
    [HarmonyPatch(typeof(Act4BossPegBoardFrameManager), nameof(Act4BossPegBoardFrameManager.RegisterCallbacks))]
    [HarmonyPostfix]
    public static void Act4BossPegBoardFrameManager_RegisterCallbacks_Postfix()
    {
        if (!IsHosting)
        {
            return;
        }

        if (_spiritRadiaHostSubscribed)
        {
            return;
        }

        _spiritRadiaHostSubscribed = true;

        global::Battle.Enemies.SpiritOfRadiaBoss.PreTransitionStarted += DispatchPreTransition;
        global::Battle.Enemies.SpiritOfRadiaBoss.OnSpiritOfRadiaPhaseTransitionStarted += DispatchMainTransition;
        MultiplayerPlugin.Logger?.LogInfo("[SpiritOfRadia] Host: subscribed to phase-2 delegates");
    }
}
