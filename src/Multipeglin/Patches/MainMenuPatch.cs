using System;
using BepInEx.Logging;
using HarmonyLib;
using Multipeglin.Multiplayer;
using Multipeglin.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Multipeglin.Patches;

public static class MenuButtonInjector
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;
    private static GameObject _multiplayerButton;

    public static void InjectIfNeeded()
    {
        try
        {
            // Already injected and still alive
            if (_multiplayerButton != null && _multiplayerButton)
                return;

            // Check if a previous injection left a button (e.g. from Harmony + SceneWatcher both firing)
            var existing = GameObject.Find("MultiplayerButton");
            if (existing != null)
            {
                _multiplayerButton = existing;
                return;
            }

            var allButtons = UnityEngine.Object.FindObjectsOfType<Button>(true);

            // Find a neutral button to clone (Credits/Options — NOT Quit which has red styling)
            Button cloneSource = null;
            Transform encirclepediaTransform = null;

            foreach (var btn in allButtons)
            {
                var nameUpper = btn.gameObject.name.ToUpperInvariant();
                if (nameUpper.Contains("ENCIRCLEPEDIA") || nameUpper.Contains("ENCYCLOPEDIA"))
                    encirclepediaTransform = btn.transform;
                if (cloneSource == null && (nameUpper.Contains("CREDIT") || nameUpper.Contains("OPTION")))
                    cloneSource = btn;
            }

            if (cloneSource == null)
            {
                Log?.LogWarning("MenuButtonInjector: no Credits/Options button found to clone");
                return;
            }

            // Clone the entire button — preserves all components, animations, hover behavior
            _multiplayerButton = UnityEngine.Object.Instantiate(cloneSource.gameObject, cloneSource.transform.parent);
            _multiplayerButton.name = "MultiplayerButton";

            // Change the text
            var tmp = _multiplayerButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
                tmp.text = "Multiplayer";

            // Remove localization so it doesn't overwrite our text
            foreach (var comp in _multiplayerButton.GetComponentsInChildren<Component>(true))
            {
                if (comp != null && comp.GetType().Name == "Localize")
                    UnityEngine.Object.Destroy(comp);
            }

            // Rewire click handler — must reset the entire onClick to clear
            // persistent (inspector-wired) listeners that RemoveAllListeners doesn't touch
            var button = _multiplayerButton.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(MultiplayerUI.ToggleOverlayStatic);
            }

            // Place above Encirclepedia
            var targetSibling = encirclepediaTransform ?? cloneSource.transform;
            _multiplayerButton.transform.SetSiblingIndex(targetSibling.GetSiblingIndex());

            Log?.LogInfo($"MenuButtonInjector: cloned '{cloneSource.gameObject.name}', placed above '{targetSibling.gameObject.name}'");
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
        MultiplayerPlugin.Logger?.LogInfo("PlayButtonAwakePatch: PlayButton.Awake fired");
        MenuButtonInjector.InjectIfNeeded();
    }
}

public class SceneWatcher : MonoBehaviour
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;
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
            {
                // If we're in a multiplayer session and land on MainMenu,
                // the game ended (death, quit, run complete). Disconnect everyone.
                var services = MultiplayerPlugin.Services;
                if (services != null && services.TryResolve<IMultiplayerMode>(out var m)
                    && (m.IsHosting || m.IsSpectating))
                {
                    Log?.LogInfo("SceneWatcher: MainMenu reached while in multiplayer — disconnecting");
                    MultiplayerSession.DisconnectAndReset("Game returned to main menu");
                }

                StartCoroutine(DelayedInject());
            }
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

/// <summary>
/// "Return to Main Menu" from the pause menu. On the client, the default path
/// (PeglinSceneLoader.LoadScene(MAIN_MENU)) is suppressed by the client-logic
/// block, so the button does nothing. Intercept and route through the
/// multiplayer disconnect flow, which tears down the session (disables
/// suppression) and then loads MainMenu properly. Also runs on the host so
/// clients receive a DisconnectEvent instead of a silently dropped peer.
/// </summary>
[HarmonyPatch(typeof(PauseMenu), nameof(PauseMenu.QuitToMenu))]
public static class PauseMenuQuitToMenuPatch
{
    public static bool Prefix(PauseMenu __instance)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null)
            return true;
        if (!services.TryResolve<IMultiplayerMode>(out var mode))
            return true;
        if (!mode.IsSpectating && !mode.IsHosting)
            return true;

        MultiplayerPlugin.Logger?.LogInfo("[PauseMenu] QuitToMenu in multiplayer — disconnecting");
        try
        { __instance.Resume(); }
        catch { }
        MultiplayerSession.DisconnectAndReset("Returned to main menu");
        return false;
    }
}
