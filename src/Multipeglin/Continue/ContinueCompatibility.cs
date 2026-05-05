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
    /// Map of current mod version -> semver range describing which older
    /// (or future) authored versions can be loaded by this version. Range
    /// uses NuGet-style interval notation: square brackets are inclusive,
    /// parentheses are exclusive. The current version is always implicitly
    /// compatible with itself; do not include it in its own range.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CONTINUE_COMPATIBILITY_LIST
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["0.1.10"] = "[0.1.8,0.1.10]",
            ["0.1.11"] = "[0.1.8,1.1.11]",
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

        if (!CONTINUE_COMPATIBILITY_LIST.TryGetValue(currentModVersion, out var range)
            || string.IsNullOrEmpty(range))
        {
            return false;
        }

        return SatisfiesRange(savedModVersion, range);
    }

    /// <summary>
    /// Parses an interval-notation range string and returns true if
    /// <paramref name="version"/> falls inside it.
    /// Supported forms: "[a,b]", "[a,b)", "(a,b]", "(a,b)" where each
    /// endpoint is a "major.minor.patch" string. Invalid or unparseable
    /// input returns false.
    /// </summary>
    internal static bool SatisfiesRange(string version, string range)
    {
        if (string.IsNullOrWhiteSpace(range) || range.Length < 5)
        {
            return false;
        }

        var startInclusive = range[0] == '[';
        var endInclusive = range[range.Length - 1] == ']';
        if (!startInclusive && range[0] != '(')
        {
            return false;
        }

        if (!endInclusive && range[range.Length - 1] != ')')
        {
            return false;
        }

        var inner = range.Substring(1, range.Length - 2);
        var comma = inner.IndexOf(',');
        if (comma < 0)
        {
            return false;
        }

        var minStr = inner.Substring(0, comma).Trim();
        var maxStr = inner.Substring(comma + 1).Trim();

        if (!TryParseSemver(version, out var v))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(minStr))
        {
            if (!TryParseSemver(minStr, out var min))
            {
                return false;
            }

            var cmp = CompareSemver(v, min);
            if (cmp < 0 || (cmp == 0 && !startInclusive))
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(maxStr))
        {
            if (!TryParseSemver(maxStr, out var max))
            {
                return false;
            }

            var cmp = CompareSemver(v, max);
            if (cmp > 0 || (cmp == 0 && !endInclusive))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseSemver(string s, out (int major, int minor, int patch) result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        var parts = s.Split('.');
        if (parts.Length < 1 || parts.Length > 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var major))
        {
            return false;
        }

        var minor = 0;
        if (parts.Length > 1 && !int.TryParse(parts[1], out minor))
        {
            return false;
        }

        var patch = 0;
        if (parts.Length > 2 && !int.TryParse(parts[2], out patch))
        {
            return false;
        }

        result = (major, minor, patch);
        return true;
    }

    private static int CompareSemver(
        (int major, int minor, int patch) a,
        (int major, int minor, int patch) b)
    {
        var c = a.major.CompareTo(b.major);
        if (c != 0)
        {
            return c;
        }

        c = a.minor.CompareTo(b.minor);
        if (c != 0)
        {
            return c;
        }

        return a.patch.CompareTo(b.patch);
    }
}
