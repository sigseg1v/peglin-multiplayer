using BepInEx.Logging;
using PeglinMods.Spectator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeglinMods.Spectator.Patches;

public static class MainMenuButtonInjector
{
    private static GameObject _multiplayerButton;
    private static string _lastScene = "";

    public static void SearchAndInject(ManualLogSource log)
    {
        // Reset on scene change
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != _lastScene)
        {
            _lastScene = scene;
            _multiplayerButton = null;
        }

        if (_multiplayerButton != null)
            return;

        // Log all buttons first
        var allButtons = Object.FindObjectsOfType<Button>();
        log?.LogInfo($"Found {allButtons.Length} buttons total");
        foreach (var b in allButtons)
        {
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            log?.LogInfo($"  Button: '{t?.text ?? "(no TMP)"}' obj='{b.gameObject.name}' parent='{b.transform.parent?.name}'");
        }

        // Look for the Quit button
        foreach (var btn in allButtons)
        {
            var text = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (text == null) continue;
            var upper = text.text.ToUpperInvariant();
            if (!upper.Contains("QUIT") && !btn.gameObject.name.ToUpperInvariant().Contains("QUIT")) continue;

            log?.LogInfo($"Found Quit button: '{text.text}' on '{btn.gameObject.name}'");

            var quitTransform = btn.transform;
            var parent = quitTransform.parent;

            _multiplayerButton = Object.Instantiate(quitTransform.gameObject, parent);
            _multiplayerButton.name = "MultiplayerButton";
            _multiplayerButton.transform.SetSiblingIndex(quitTransform.GetSiblingIndex());

            var tmpText = _multiplayerButton.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null)
                tmpText.text = "Multiplayer";

            // Remove localization that would overwrite text
            foreach (var comp in _multiplayerButton.GetComponentsInChildren<Component>())
            {
                if (comp != null && comp.GetType().Name == "Localize")
                    Object.Destroy(comp);
            }

            var button = _multiplayerButton.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(MultiplayerUI.ToggleOverlayStatic);

            log?.LogInfo("Multiplayer menu button added!");
            return;
        }
    }
}
