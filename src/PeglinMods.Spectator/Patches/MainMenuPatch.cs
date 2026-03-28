using System;
using BepInEx.Logging;
using HarmonyLib;
using PeglinMods.Spectator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PeglinMods.Spectator.Patches;

/// <summary>
/// Injects a "Multiplayer" button into the main menu by cloning the Quit button.
/// Two mechanisms for reliability:
///   1. Harmony postfix on PlayButton.Awake (fires at exact right time)
///   2. SceneWatcher with Update() scene polling (fallback)
/// </summary>
public static class MenuButtonInjector
{
    private static ManualLogSource Log => SpectatorPlugin.Logger;
    private static GameObject _multiplayerButton;

    public static void InjectIfNeeded()
    {
        try
        {
            if (_multiplayerButton != null && _multiplayerButton) return;
            _multiplayerButton = null;

            var allButtons = UnityEngine.Object.FindObjectsOfType<Button>(true);
            Log?.LogInfo($"MenuButtonInjector: scanning {allButtons.Length} buttons");

            foreach (var btn in allButtons)
            {
                var text = btn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (text == null) continue;

                var upper = text.text.ToUpperInvariant();
                var nameUpper = btn.gameObject.name.ToUpperInvariant();
                if (!upper.Contains("QUIT") && !nameUpper.Contains("QUIT")) continue;

                Log?.LogInfo($"MenuButtonInjector: cloning '{btn.gameObject.name}'");

                var quitTransform = btn.transform;
                var parent = quitTransform.parent;

                _multiplayerButton = UnityEngine.Object.Instantiate(quitTransform.gameObject, parent);
                _multiplayerButton.name = "MultiplayerButton";
                _multiplayerButton.transform.SetSiblingIndex(quitTransform.GetSiblingIndex());

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

            Log?.LogWarning("MenuButtonInjector: no Quit button found");
        }
        catch (Exception ex)
        {
            Log?.LogError($"MenuButtonInjector: {ex}");
        }
    }

    public static void Reset()
    {
        _multiplayerButton = null;
    }
}

/// <summary>
/// Harmony postfix on PlayButton.Awake - fires when main menu is ready.
/// </summary>
[HarmonyPatch(typeof(PeglinUI.MainMenu.PlayButton), "Awake")]
public static class PlayButtonAwakePatch
{
    public static void Postfix()
    {
        SpectatorPlugin.Logger?.LogInfo("PlayButtonAwakePatch: PlayButton.Awake fired");
        MenuButtonInjector.InjectIfNeeded();
    }
}

/// <summary>
/// Scene watcher on a persistent HideAndDontSave GameObject.
/// Uses both SceneManager.sceneLoaded and Update() polling.
/// </summary>
public class SceneWatcher : MonoBehaviour
{
    private static ManualLogSource Log => SpectatorPlugin.Logger;
    private string _lastScene = "";
    private float _injectTimer = -1f;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        try
        {
            var currentScene = SceneManager.GetActiveScene().name;
            if (currentScene != _lastScene)
            {
                Log?.LogInfo($"SceneWatcher: scene change '{_lastScene}' -> '{currentScene}'");
                _lastScene = currentScene;
                MenuButtonInjector.Reset();

                if (currentScene == "MainMenu")
                    _injectTimer = 0.5f;
            }

            if (_injectTimer > 0f)
            {
                _injectTimer -= Time.deltaTime;
                if (_injectTimer <= 0f)
                {
                    _injectTimer = -1f;
                    MenuButtonInjector.InjectIfNeeded();
                }
            }
        }
        catch { }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            Log?.LogInfo($"SceneWatcher: scene '{scene.name}' loaded");
            _lastScene = scene.name;
            MenuButtonInjector.Reset();

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
