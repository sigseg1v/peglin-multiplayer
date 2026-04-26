using HarmonyLib;
using static Multipeglin.Patches.MultiplayerClientPatches;

namespace Multipeglin.Patches;

[HarmonyPatch]
internal static class RandomPegFieldPatches
{
    /// <summary>
    /// Block RandomPegField's per-turn peg repositioning on client.
    /// When moveEveryTurn is true, RandomPegField.TurnComplete re-randomizes all
    /// peg positions using client-side RNG — causing layout divergence every turn.
    /// The host's periodic sync will send correct positions.
    /// </summary>
    [HarmonyPatch(typeof(Battle.PegBehaviour.RandomPegField), "TurnComplete")]
    [HarmonyPrefix]
    public static bool RandomPegField_TurnComplete_Prefix()
    {
        if (!ShouldSuppressClientLogic)
        {
            return true;
        }

        return false;
    }
}
