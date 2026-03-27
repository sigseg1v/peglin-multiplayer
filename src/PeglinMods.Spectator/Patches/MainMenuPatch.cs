using BepInEx.Logging;
using PeglinMods.Spectator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PeglinMods.Spectator.Patches;

/// <summary>
/// Manages scene change detection and menu button injection.
/// Attached as a MonoBehaviour to the persistent PeglinMods_Spectator GameObject.
/// Uses SceneManager.sceneLoaded (the standard Unity pattern used by ProLib/existing Peglin mods).
/// </summary>
public class SceneWatcher : MonoBehaviour
{
    private static ManualLogSource Log => SpectatorPlugin.Logger;
    private GameObject _multiplayerButton;

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
        Log?.LogInfo($"SceneWatcher: scene loaded '{scene.name}' (mode={mode})");

        // Reset button reference on any scene change
        _multiplayerButton = null;

        if (scene.name == "MainMenu")
        {
            // Wait one frame for all scene objects to initialize
            StartCoroutine(InjectMenuButtonDelayed());
        }
    }

    private System.Collections.IEnumerator InjectMenuButtonDelayed()
    {
        yield return null; // Wait one frame
        yield return null; // Wait a second frame for good measure (localization, etc.)
        InjectMenuButton();
    }

    private void InjectMenuButton()
    {
        if (_multiplayerButton != null) return;

        var allButtons = FindObjectsOfType<Button>(true);
        Log?.LogInfo($"SceneWatcher: scanning {allButtons.Length} buttons in MainMenu");

        foreach (var btn in allButtons)
        {
            var text = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text == null) continue;

            Log?.LogInfo($"  Button: '{text.text}' obj='{btn.gameObject.name}' active={btn.gameObject.activeInHierarchy}");

            // Find the Quit button to clone it (ensures matching style)
            var upper = text.text.ToUpperInvariant();
            var nameUpper = btn.gameObject.name.ToUpperInvariant();
            if (!upper.Contains("QUIT") && !nameUpper.Contains("QUIT")) continue;

            Log?.LogInfo($"SceneWatcher: cloning Quit button '{btn.gameObject.name}'");

            var quitTransform = btn.transform;
            var parent = quitTransform.parent;

            _multiplayerButton = Instantiate(quitTransform.gameObject, parent);
            _multiplayerButton.name = "MultiplayerButton";

            // Place it above the Quit button
            _multiplayerButton.transform.SetSiblingIndex(quitTransform.GetSiblingIndex());

            // Update text
            var tmpText = _multiplayerButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmpText != null)
                tmpText.text = "Multiplayer";

            // Remove localization components that would overwrite our text
            foreach (var comp in _multiplayerButton.GetComponentsInChildren<Component>(true))
            {
                if (comp != null && comp.GetType().Name.Contains("Localize"))
                {
                    Log?.LogInfo($"  Removing localization component: {comp.GetType().Name}");
                    Destroy(comp);
                }
            }

            // Rewire button
            var button = _multiplayerButton.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(MultiplayerUI.ToggleOverlayStatic);

            Log?.LogInfo("SceneWatcher: Multiplayer button injected into main menu!");
            return;
        }

        Log?.LogWarning("SceneWatcher: could not find Quit button to clone");
    }
}
