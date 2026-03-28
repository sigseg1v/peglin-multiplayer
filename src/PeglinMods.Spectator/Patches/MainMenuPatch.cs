using System;
using BepInEx.Logging;
using HarmonyLib;
using PeglinMods.Spectator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PeglinMods.Spectator.Patches;

public static class MenuButtonInjector
{
    private static ManualLogSource Log => SpectatorPlugin.Logger;
    private static GameObject _multiplayerButton;

    public static void InjectIfNeeded()
    {
        try
        {
            // Already injected and still alive
            if (_multiplayerButton != null && _multiplayerButton) return;

            // Check if a previous injection left a button (e.g. from Harmony + SceneWatcher both firing)
            var existing = GameObject.Find("MultiplayerButton");
            if (existing != null)
            {
                _multiplayerButton = existing;
                return;
            }

            var allButtons = UnityEngine.Object.FindObjectsOfType<Button>(true);

            // Find a menu button to clone (prefer Quit for styling)
            Button templateBtn = null;
            Transform encirclepediaTransform = null;

            foreach (var btn in allButtons)
            {
                var nameUpper = btn.gameObject.name.ToUpperInvariant();
                if (nameUpper.Contains("QUIT"))
                    templateBtn = btn;
                if (nameUpper.Contains("ENCIRCLEPEDIA") || nameUpper.Contains("ENCYCLOPEDIA"))
                    encirclepediaTransform = btn.transform;
            }

            if (templateBtn != null)
            {
                var parent = templateBtn.transform.parent;

                // DON'T clone the Quit button - it has child objects, animations,
                // and scripts that trigger Application.Quit() even after stripping.
                // Create a fresh button from scratch and match the layout.
                _multiplayerButton = new GameObject("MultiplayerButton");
                _multiplayerButton.transform.SetParent(parent, false);

                // Copy RectTransform sizing from template
                var templateRect = templateBtn.GetComponent<RectTransform>();
                var rect = _multiplayerButton.AddComponent<RectTransform>();
                rect.sizeDelta = templateRect.sizeDelta;

                // Copy visual style from template
                var templateImg = templateBtn.GetComponent<Image>();
                var img = _multiplayerButton.AddComponent<Image>();
                if (templateImg != null)
                {
                    img.sprite = templateImg.sprite;
                    img.type = templateImg.type;
                    img.color = templateImg.color;
                    img.material = templateImg.material;
                }

                // Add button with matching color block
                var btn = _multiplayerButton.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.colors = templateBtn.colors;
                btn.onClick.AddListener(MultiplayerUI.ToggleOverlayStatic);

                // Create text child matching the template's text style
                var templateText = templateBtn.GetComponentInChildren<TextMeshProUGUI>(true);
                var textObj = new GameObject("Text");
                textObj.transform.SetParent(_multiplayerButton.transform, false);
                var tmp = textObj.AddComponent<TextMeshProUGUI>();
                tmp.text = "Multiplayer";
                tmp.alignment = TextAlignmentOptions.Center;
                if (templateText != null)
                {
                    tmp.font = templateText.font;
                    tmp.fontSize = templateText.fontSize;
                    tmp.fontStyle = templateText.fontStyle;
                    tmp.color = templateText.color;
                    tmp.material = templateText.material;
                }
                var textRect = textObj.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;

                // Place above Encirclepedia if found, otherwise above Quit
                var targetSibling = encirclepediaTransform ?? templateBtn.transform;
                _multiplayerButton.transform.SetSiblingIndex(targetSibling.GetSiblingIndex());

                Log?.LogInfo($"MenuButtonInjector: created fresh button above '{targetSibling.gameObject.name}'");
                return;
            }
        }
        catch (Exception ex)
        {
            Log?.LogError($"MenuButtonInjector: {ex}");
        }
    }

    public static void OnSceneChanged()
    {
        _multiplayerButton = null;
    }
}

[HarmonyPatch(typeof(PeglinUI.MainMenu.PlayButton), "Awake")]
public static class PlayButtonAwakePatch
{
    public static void Postfix()
    {
        SpectatorPlugin.Logger?.LogInfo("PlayButtonAwakePatch: PlayButton.Awake fired");
        MenuButtonInjector.InjectIfNeeded();
    }
}

public class SceneWatcher : MonoBehaviour
{
    private static ManualLogSource Log => SpectatorPlugin.Logger;
    private string _lastScene = "";

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            Log?.LogInfo($"SceneWatcher: scene '{scene.name}' loaded");
            if (scene.name != _lastScene)
            {
                MenuButtonInjector.OnSceneChanged();
                _lastScene = scene.name;
            }

            if (scene.name == "MainMenu")
                StartCoroutine(DelayedInject());
        }
        catch { }
    }

    private System.Collections.IEnumerator DelayedInject()
    {
        yield return null;
        yield return null;
        MenuButtonInjector.InjectIfNeeded();
    }
}
