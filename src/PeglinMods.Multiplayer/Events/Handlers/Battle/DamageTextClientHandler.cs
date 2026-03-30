using System;
using PeglinMods.Multiplayer.Events.Network.Battle;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;
using TMPro;

namespace PeglinMods.Multiplayer.Events.Handlers.Battle;

public sealed class DamageTextClientHandler : IClientHandler<DamageTextEvent>
{
    public void Handle(DamageTextEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            // Find the DamageCountDisplay to use its text pool
            var dcd = UnityEngine.Object.FindObjectOfType<DamageCountDisplay>();
            if (dcd != null)
            {
                var pos = new Vector3(e.PosX, e.PosY, 0f);
                var color = new Color(e.R, e.G, e.B, e.A);
                try
                {
                    // Use reflection to call the internal CreateText method
                    var method = HarmonyLib.AccessTools.Method(typeof(DamageCountDisplay), "CreateText");
                    method?.Invoke(dcd, new object[] { e.Text, pos, color });
                }
                catch
                {
                    // Fallback: create simple floating text
                    CreateSimpleDamageText(e.Text, pos, color, e.Scale);
                }
            }
            else
            {
                // No DamageCountDisplay — create standalone text
                var pos = new Vector3(e.PosX, e.PosY, 0f);
                var color = new Color(e.R, e.G, e.B, e.A);
                CreateSimpleDamageText(e.Text, pos, color, e.Scale);
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"DamageText handler failed: {ex.Message}");
        }
    }

    private static void CreateSimpleDamageText(string text, Vector3 pos, Color color, float scale)
    {
        var go = new GameObject("DamageText");
        go.transform.position = pos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.color = color;
        tmp.fontSize = 5f * scale;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.sortingOrder = 200;
        UnityEngine.Object.Destroy(go, 1.5f);
    }
}
