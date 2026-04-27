using System;
using System.Collections.Generic;
using System.Linq;

namespace Multipeglin.Continue;

/// <summary>
/// Host-side context for an in-progress "Continue" lobby. When set, the lobby
/// only allows clients whose names match the original roster, places each one
/// into their saved slot (not join-order), and refuses to start until the
/// exact expected set is present.
/// </summary>
public static class ContinueSession
{
    /// <summary>The save we're loading from. Null when no continue is in progress.</summary>
    public static ContinueSaveData ActiveSave { get; private set; }

    /// <summary>Path the active save was loaded from.</summary>
    public static string ActiveSavePath { get; private set; }

    /// <summary>
    /// Sanitized-name -> slot index. Sanitized via ContinueFiles.Sanitize so
    /// matching is case-aware but tolerant of cosmetic punctuation differences
    /// (mirrors the file-name canonicalization).
    /// </summary>
    private static Dictionary<string, int> _expectedNameToSlot =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public static bool IsActive => ActiveSave != null;

    /// <summary>Begin a continue session. Host calls this when the user clicks a save row.</summary>
    public static void Begin(ContinueSaveData data, string filePath)
    {
        ActiveSave = data ?? throw new ArgumentNullException(nameof(data));
        ActiveSavePath = filePath;
        _expectedNameToSlot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (data.Players != null)
        {
            foreach (var p in data.Players)
            {
                var key = ContinueFiles.Sanitize(p.PlayerName ?? string.Empty);
                _expectedNameToSlot[key] = p.SlotIndex;
            }
        }
    }

    /// <summary>Drop the active continue session (e.g., user backs out of lobby).</summary>
    public static void Clear()
    {
        ActiveSave = null;
        ActiveSavePath = null;
        _expectedNameToSlot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// If a continue is active, return the saved slot index for the given player
    /// name, or -1 if the name is not in the expected roster.
    /// Returns -1 (do nothing special) when no continue is active.
    /// </summary>
    public static int GetSlotForPlayer(string playerName)
    {
        if (!IsActive)
        {
            return -1;
        }

        var key = ContinueFiles.Sanitize(playerName ?? string.Empty);
        return _expectedNameToSlot.TryGetValue(key, out var slot) ? slot : -1;
    }

    /// <summary>True if this name was on the saved roster.</summary>
    public static bool IsExpected(string playerName) => GetSlotForPlayer(playerName) >= 0;

    /// <summary>
    /// Saved class index for the given player name, or 0 (Peglin) if not found.
    /// Class is locked in continue mode — saved players keep their original class.
    /// </summary>
    public static int GetClassForPlayer(string playerName)
    {
        if (!IsActive || ActiveSave?.Players == null)
        {
            return 0;
        }

        var key = ContinueFiles.Sanitize(playerName ?? string.Empty);
        foreach (var p in ActiveSave.Players)
        {
            if (string.Equals(ContinueFiles.Sanitize(p.PlayerName ?? string.Empty), key, StringComparison.OrdinalIgnoreCase))
            {
                return p.ChosenClass;
            }
        }

        return 0;
    }

    /// <summary>The set of sanitized names we're waiting for.</summary>
    public static IReadOnlyCollection<string> ExpectedSanitizedNames => _expectedNameToSlot.Keys;

    /// <summary>Total expected player count from the save.</summary>
    public static int ExpectedPlayerCount => _expectedNameToSlot.Count;

    /// <summary>
    /// True iff every expected player name is present in the supplied roster.
    /// Names are compared via the sanitization rules used for file naming.
    /// </summary>
    public static bool AllExpectedPlayersPresent(IEnumerable<string> presentNames)
    {
        if (!IsActive)
        {
            return true;
        }

        if (presentNames == null)
        {
            return _expectedNameToSlot.Count == 0;
        }

        var present = new HashSet<string>(
            presentNames.Where(n => !string.IsNullOrEmpty(n)).Select(ContinueFiles.Sanitize),
            StringComparer.OrdinalIgnoreCase);

        return _expectedNameToSlot.Keys.All(present.Contains);
    }
}
