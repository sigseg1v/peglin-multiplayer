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
        SceneWatcher.DiagWrite("PlayButtonAwakePatch.Postfix() FIRED!");
        SpectatorPlugin.Logger?.LogInfo("PlayButtonAwakePatch: PlayButton.Awake fired");
        MenuButtonInjector.InjectIfNeeded();
    }
}

/// <summary>
/// Fallback A: SceneManager.sceneLoaded on a persistent MonoBehaviour.
/// </summary>
public class SceneWatcher : MonoBehaviour
{
    private static ManualLogSource Log => SpectatorPlugin.Logger;
    private static readonly string DiagFile = System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
        "scenewatcher_diag.txt");
    private string _lastScene = "";
    private float _injectTimer = -1f;
    private bool _loggedUpdate;
    private int _frameCount;

    internal static void DiagWrite(string msg)
    {
        try { System.IO.File.AppendAllText(DiagFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
        catch { }
    }

    private void Awake()
    {
        DiagWrite($"SceneWatcher.Awake() called on '{gameObject.name}' active={gameObject.activeSelf}");
    }

    private void OnEnable()
    {
        DiagWrite("SceneWatcher.OnEnable() called");
        SceneManager.sceneLoaded += OnSceneLoaded;
        Log?.LogInfo("SceneWatcher: subscribed to SceneManager.sceneLoaded");
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// Fallback B: Update() poll. Checks active scene name every frame.
    /// If we're in MainMenu and haven't injected yet, inject.
    /// This is the nuclear option - if SceneManager.sceneLoaded and
    /// Harmony both fail, this still works because Update() on a
    /// DontDestroyOnLoad MonoBehaviour always fires.
    /// </summary>
    private void Update()
    {
        _frameCount++;
        if (!_loggedUpdate)
        {
            _loggedUpdate = true;
            DiagWrite($"Update() first call! frame={_frameCount}");
            Log?.LogInfo("SceneWatcher: Update() is firing (MonoBehaviour lifecycle works)");
        }
        if (_frameCount % 300 == 0) // Every ~5 seconds at 60fps
        {
            DiagWrite($"Update() heartbeat frame={_frameCount} scene={SceneManager.GetActiveScene().name}");
        }

        // Timer-based injection after scene change detection
        if (_injectTimer >= 0f)
        {
            _injectTimer -= Time.deltaTime;
            if (_injectTimer <= 0f)
            {
                _injectTimer = -1f;
                Log?.LogInfo("SceneWatcher: timer-based injection firing");
                MenuButtonInjector.InjectIfNeeded();
            }
            return;
        }

        // Poll-based scene detection
        try
        {
            var currentScene = SceneManager.GetActiveScene().name;
            if (currentScene != _lastScene)
            {
                Log?.LogInfo($"SceneWatcher: detected scene change '{_lastScene}' -> '{currentScene}' (via Update poll)");
                _lastScene = currentScene;
                MenuButtonInjector.Reset();

                if (currentScene == "MainMenu")
                {
                    // Wait 0.5 seconds for scene objects to fully initialize
                    _injectTimer = 0.5f;
                }
            }
        }
        catch (Exception ex)
        {
            Log?.LogDebug($"SceneWatcher.Update scene poll: {ex.Message}");
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            Log?.LogInfo($"SceneWatcher: scene '{scene.name}' loaded (mode={mode}) [via sceneLoaded event]");
            MenuButtonInjector.Reset();
            _lastScene = scene.name;

            if (scene.name == "MainMenu")
            {
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
