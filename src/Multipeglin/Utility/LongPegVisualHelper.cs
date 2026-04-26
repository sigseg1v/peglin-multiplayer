using HarmonyLib;
using UnityEngine;

namespace Multipeglin.Utility;

/// <summary>
/// Helpers for replicating LongPeg's host-side hit visual on the client.
///
/// The host's LongPeg.PegActivated flow:
///   1. Sets _cleared=true, _hit=true.
///   2. Picks material (_activeMaterial unless _poppedPegTrigger is enabled,
///      in which case _destroyedMaterial).
///   3. Applies _colors.Hit (gray) to the chosen material.
///
/// The client cannot call PegActivated directly because that path runs relic
/// effects, status effects, and the OnPegActivated delegate — all of which
/// must stay host-authoritative. This helper sets the same visual state via
/// reflection without invoking game logic.
/// </summary>
public static class LongPegVisualHelper
{
    public static void ApplyHitVisual(LongPeg peg)
    {
        if (peg == null)
        {
            return;
        }

        try
        {
            var hitField = AccessTools.Field(typeof(LongPeg), "_hit");
            var clearedField = AccessTools.Field(typeof(global::Peg), "_cleared");
            var rendererField = AccessTools.Field(typeof(LongPeg), "_renderer");
            var colorsField = AccessTools.Field(typeof(LongPeg), "_colors");
            var activeMatField = AccessTools.Field(typeof(LongPeg), "_activeMaterial");
            var destroyedMatField = AccessTools.Field(typeof(LongPeg), "_destroyedMaterial");
            var poppedTriggerField = AccessTools.Field(typeof(global::Peg), "_poppedPegTrigger");

            hitField?.SetValue(peg, true);
            clearedField?.SetValue(peg, true);

            var renderer = rendererField?.GetValue(peg) as MeshRenderer;
            if (renderer == null)
            {
                return;
            }

            var poppedTrigger = poppedTriggerField?.GetValue(peg) as Collider2D;
            var useDestroyed = poppedTrigger != null && poppedTrigger.enabled;

            var matField = useDestroyed ? destroyedMatField : activeMatField;
            var mat = matField?.GetValue(peg) as Material;
            if (mat != null)
            {
                renderer.material = mat;
            }

            var colors = colorsField?.GetValue(peg);
            if (colors != null)
            {
                var hitColorField = colors.GetType().GetField("Hit");
                if (hitColorField != null && renderer.material != null)
                {
                    var hitColor = (Color)hitColorField.GetValue(colors);
                    renderer.material.color = hitColor;
                }
            }
        }
        catch { }
    }
}
