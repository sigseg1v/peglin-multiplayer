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

                _multiplayerButton = UnityEngine.Object.Instantiate(templateBtn.gameObject, parent);
                _multiplayerButton.name = "MultiplayerButton";

                // Place above Encirclepedia if found, otherwise above Quit
                var targetSibling = encirclepediaTransform ?? templateBtn.transform;
                _multiplayerButton.transform.SetSiblingIndex(targetSibling.GetSiblingIndex());

                Log?.LogInfo($"MenuButtonInjector: placed above '{targetSibling.gameObject.name}'");

                var tmpText = _multiplayerButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmpText != null)
                    tmpText.text = "Multiplayer";

                foreach (var comp in _multiplayerButton.GetComponentsInChildren<Component>(true))
                {
                    if (comp != null && comp.GetType().Name.Contains("Localize"))
                        UnityEngine.Object.Destroy(comp);
                }

                var button = _multiplayerButton.GetComponent<Button>();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(MultiplayerUI.ToggleOverlayStatic);

                Log?.LogInfo("MenuButtonInjector: Multiplayer button injected!");
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
