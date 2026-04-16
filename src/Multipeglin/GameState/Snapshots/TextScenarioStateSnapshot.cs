using System.Collections.Generic;

namespace Multipeglin.GameState.Snapshots;

/// <summary>
/// Captures the current state of a TextScenario dialogue for syncing to spectating clients.
/// </summary>
public class TextScenarioStateSnapshot
{
    /// <summary>Whether a dialogue conversation is currently active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Current NPC dialogue/subtitle text.</summary>
    public string SubtitleText { get; set; }

    /// <summary>Name of the speaking character.</summary>
    public string SpeakerName { get; set; }

    /// <summary>Available response button texts (empty when no choices are shown).</summary>
    public List<string> Responses { get; set; } = new List<string>();

    /// <summary>Index of the response the host is hovering (-1 = none).</summary>
    public int HighlightedIndex { get; set; } = -1;

    /// <summary>Whether the post-dialogue navigation phase is active (host is shooting at pegboard).</summary>
    public bool IsNavigating { get; set; }

    /// <summary>Whether this is the Mirror event (client gets interactive version).</summary>
    public bool IsMirrorEvent { get; set; }

    /// <summary>The scenario asset name (used for loading on client).</summary>
    public string ScenarioName { get; set; }
}
