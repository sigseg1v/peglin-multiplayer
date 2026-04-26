using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Multipeglin.GameState.Snapshots;
using PixelCrushers.DialogueSystem;
using UnityEngine.SceneManagement;

namespace Multipeglin.GameState.Providers;

public class TextScenarioStateProvider
{
    private readonly ManualLogSource _log;

    public TextScenarioStateProvider(ManualLogSource log) => _log = log;

    /// <summary>
    /// Capture the current TextScenario dialogue state for syncing to clients.
    /// Returns null if not on a TextScenario scene.
    /// </summary>
    public TextScenarioStateSnapshot Capture()
    {
        try
        {
            var currentScene = SceneManager.GetActiveScene().name;
            if (currentScene != "TextScenario")
            {
                return null;
            }

            var snapshot = new TextScenarioStateSnapshot
            {
                IsActive = DialogueManager.isConversationActive,
                IsNavigating = TextScenarioHoverTracker.IsNavigating,
                HighlightedIndex = TextScenarioHoverTracker.CurrentHoveredIndex,
            };

            // Get the scenario name from the active MapDataScenario
            var dss = UnityEngine.Object.FindObjectOfType<RNG.Scenarios.DialogueSystemScenario>();
            if (dss != null)
            {
                var mapData = HarmonyLib.AccessTools.Field(typeof(RNG.Scenarios.DialogueSystemScenario), "_activeMapData")
                    ?.GetValue(dss) as Data.Scenarios.MapDataScenario;
                if (mapData != null)
                {
                    snapshot.ScenarioName = mapData.scenarioName;
                    // Detect mirror event by scenario name
                    snapshot.IsMirrorEvent = IsMirrorScenario(mapData.scenarioName);
                }
            }

            // Read dialogue text and responses if conversation is active
            if (snapshot.IsActive)
            {
                CaptureDialogueState(snapshot);
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[TextScenarioProvider] Capture failed: {ex.Message}");
            return null;
        }
    }

    private void CaptureDialogueState(TextScenarioStateSnapshot snapshot)
    {
        try
        {
            // Find the StandardDialogueUI in the scene
            var dialogueUI = UnityEngine.Object.FindObjectOfType<StandardDialogueUI>();
            if (dialogueUI == null)
            {
                return;
            }

            var uiElements = dialogueUI.conversationUIElements;
            if (uiElements == null)
            {
                return;
            }

            // Read NPC subtitle text
            var npcPanel = uiElements.defaultNPCSubtitlePanel;
            if (npcPanel != null)
            {
                var subtitle = npcPanel.currentSubtitle;
                if (subtitle != null)
                {
                    snapshot.SubtitleText = subtitle.formattedText?.text ?? "";
                    snapshot.SpeakerName = npcPanel.portraitActorName ?? "";
                }
                else
                {
                    snapshot.SubtitleText = npcPanel.subtitleText?.text ?? "";
                    snapshot.SpeakerName = npcPanel.portraitName?.text ?? "";
                }
            }

            // Read response buttons
            var menuPanel = uiElements.defaultMenuPanel;
            if (menuPanel?.buttons != null)
            {
                snapshot.Responses = new List<string>();
                foreach (var btn in menuPanel.buttons)
                {
                    if (btn != null && btn.gameObject.activeInHierarchy && btn.isVisible)
                    {
                        snapshot.Responses.Add(btn.text ?? "");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[TextScenarioProvider] CaptureDialogueState failed: {ex.Message}");
        }
    }

    private static bool IsMirrorScenario(string scenarioName)
    {
        if (string.IsNullOrEmpty(scenarioName))
        {
            return false;
        }
        // The mirror event scenario name — check common patterns
        return scenarioName.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
