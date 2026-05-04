using System;
using System.Collections.Generic;

namespace Multipeglin.Continue;

/// <summary>
/// Backward-compatibility table for continue saves. By default, a continue
/// save is only usable on the exact mod version that produced it. This table
/// lets us declare older versions whose on-disk schema is still compatible
/// with the current loader, so a 0.1.10 client can pick up a 0.1.8 / 0.1.9
/// save without forcing the user to roll back the mod.
///
/// Add an entry here whenever a release ships that is *backward* compatible
/// with prior saves. If a release changes the schema in a load-breaking way,
/// just don't list the old versions — the strict check kicks back in and
/// hides them from the UI.
/// </summary>
public static class ContinueCompatibility
{
    /// <summary>
    /// Map of current mod version -> set of older mod versions whose saves
    /// the current version can load. The current version is always implicitly
    /// compatible with itself; do not list it as its own entry.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, HashSet<string>> CONTINUE_COMPATIBILITY_LIST
        = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["0.1.10"] = new HashSet<string>(StringComparer.Ordinal) { "0.1.8", "0.1.9" },
        };

    /// <summary>
    /// True if a save authored by <paramref name="savedModVersion"/> is loadable
    /// by a host running <paramref name="currentModVersion"/>. Same-version is
    /// always true; otherwise we consult <see cref="CONTINUE_COMPATIBILITY_LIST"/>.
    /// </summary>
    public static bool IsCompatible(string currentModVersion, string savedModVersion)
    {
        if (string.Equals(currentModVersion, savedModVersion, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrEmpty(currentModVersion) || string.IsNullOrEmpty(savedModVersion))
        {
            return false;
        }

        if (CONTINUE_COMPATIBILITY_LIST.TryGetValue(currentModVersion, out var compatibleOlder)
            && compatibleOlder != null
            && compatibleOlder.Contains(savedModVersion))
        {
            return true;
        }

        return false;
    }
}
