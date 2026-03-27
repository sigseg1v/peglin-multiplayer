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
/// Injects a "Multiplayer" button into the main menu.
/// Two mechanisms for reliability:
///   1. Harmony postfix on PlayButton.Awake (primary - fires at exact right time)
///   2. SceneWatcher MonoBehaviour with SceneManager.sceneLoaded (fallback)
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

                Log?.LogDebug($"  Button: '{text.text}' obj='{btn.gameObject.name}'");

                // Find Quit button to clone (ensures matching style/layout)
                var upper = text.text.ToUpperInvariant();
                var nameUpper = btn.gameObject.name.ToUpperInvariant();
                if (!upper.Contains("QUIT") && !nameUpper.Contains("QUIT")) continue;

                Log?.LogInfo($"MenuButtonInjector: cloning '{btn.gameObject.name}'");

                var quitTransform = btn.transform;
                var parent = quitTransform.parent;

                _multiplayerButton = UnityEngine.Object.Instantiate(quitTransform.gameObject, parent);
                _multiplayerButton.name = "MultiplayerButton";
                _multiplayerButton.transform.SetSiblingIndex(quitTransform.GetSiblingIndex());

                // Update text
                var tmpText = _multiplayerButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmpText != null)
                    tmpText.text = "Multiplayer";

                // Remove localization that would overwrite our text
                foreach (var comp in _multiplayerButton.GetComponentsInChildren<Component>(true))
                {
                    if (comp != null && comp.GetType().Name.Contains("Localize"))
                        UnityEngine.Object.Destroy(comp);
                }

                // Rewire button click
                var button = _multiplayerButton.GetComponent<Button>();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(MultiplayerUI.ToggleOverlayStatic);

                Log?.LogInfo("MenuButtonInjector: Multiplayer button injected!");
                return;
            }

            Log?.LogWarning("MenuButtonInjector: no Quit button found to clone");
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
/// Primary injection: Harmony postfix on PlayButton.Awake.
/// Fires at exactly the right time when the main menu is ready.
/// This is the same pattern the Morbs mod uses successfully.
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
/// Fallback injection: SceneManager.sceneLoaded on a persistent MonoBehaviour.
/// Fires when any scene loads. If Harmony patch on PlayButton fails (e.g. class
/// renamed in a game update), this still works.
/// </summary>
public class SceneWatcher : MonoBehaviour
{
    private static ManualLogSource Log => SpectatorPlugin.Logger;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Log?.LogInfo("SceneWatcher: subscribed to SceneManager.sceneLoaded");
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            Log?.LogInfo($"SceneWatcher: scene '{scene.name}' loaded (mode={mode})");
            MenuButtonInjector.Reset();

            if (scene.name == "MainMenu")
            {
                // Delay 2 frames then try injection (in case Harmony patch didn't fire)
                StartCoroutine(DelayedInject());
            }
        }
        catch (Exception ex)
        {
            Log?.LogError($"SceneWatcher.OnSceneLoaded: {ex}");
        }
    }

    private System.Collections.IEnumerator DelayedInject()
    {
        yield return null;
        yield return null;
        MenuButtonInjector.InjectIfNeeded();
    }
}
