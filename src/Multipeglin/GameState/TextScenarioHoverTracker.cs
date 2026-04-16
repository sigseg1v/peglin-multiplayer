namespace Multipeglin.GameState;

/// <summary>
/// Static tracker for the host's currently hovered dialogue response button index.
/// Set by Harmony postfix on StandardUIResponseButton.OnSelect.
/// Read by TextScenarioStateProvider during heartbeat capture.
/// </summary>
public static class TextScenarioHoverTracker
{
    /// <summary>Index of the response button the host is hovering. -1 = none.</summary>
    public static int CurrentHoveredIndex = -1;

    /// <summary>Whether the navigation phase is active (post-dialogue pegboard shot).</summary>
    public static bool IsNavigating;

    public static void Reset()
    {
        CurrentHoveredIndex = -1;
        IsNavigating = false;
    }
}
