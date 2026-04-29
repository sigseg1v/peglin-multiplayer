using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json;

namespace Multipeglin.Continue;

/// <summary>
/// File-system layout and naming for continue save files. Files live under
/// BepInEx/plugins/Multipeglin/continues/ and are named
/// continue-{sanitized-names-sorted}-{seed}.save.
/// </summary>
public static class ContinueFiles
{
    public const string DIRECTORY_NAME = "continues";

    public const string FILE_PREFIX = "continue-";

    public const string FILE_SUFFIX = ".save";

    public static string ContinuesDirectory
    {
        get
        {
            var path = Path.Combine(Paths.PluginPath, "Multipeglin", DIRECTORY_NAME);
            try
            {
                Directory.CreateDirectory(path);
            }
            catch
            {
            }

            return path;
        }
    }

    /// <summary>
    /// Replace every non-alphanumeric character in the player name with '_'.
    /// Empty names become "_".
    /// </summary>
    public static string Sanitize(string playerName)
    {
        if (string.IsNullOrEmpty(playerName))
        {
            return "_";
        }

        var sb = new StringBuilder(playerName.Length);
        foreach (var c in playerName)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        return sb.Length == 0 ? "_" : sb.ToString();
    }

    /// <summary>
    /// Build the canonical file name for a set of player names + seed.
    /// Names are sanitized then sorted case-insensitive.
    /// </summary>
    public static string BuildFileName(IEnumerable<string> playerNames, string seed)
    {
        if (playerNames == null)
        {
            playerNames = Array.Empty<string>();
        }

        var sanitized = playerNames
            .Select(Sanitize)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var names = sanitized.Count > 0 ? string.Join("-", sanitized) : "_";
        var safeSeed = Sanitize(seed ?? "0");
        return $"{FILE_PREFIX}{names}-{safeSeed}{FILE_SUFFIX}";
    }

    public static string BuildFilePath(IEnumerable<string> playerNames, string seed)
        => Path.Combine(ContinuesDirectory, BuildFileName(playerNames, seed));

    /// <summary>
    /// Best-effort delete of the canonical continue file for a roster + seed.
    /// Called when a run finishes (victory or full-party defeat) so the save
    /// doesn't linger past the run it belongs to. No-op if the file doesn't
    /// exist or can't be deleted.
    /// </summary>
    public static bool DeleteForRoster(IEnumerable<string> playerNames, string seed)
    {
        try
        {
            var path = BuildFilePath(playerNames, seed);
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// Atomically write a continue save (write to .tmp then rename).
    /// </summary>
    public static void Write(string filePath, ContinueSaveData data)
    {
        var json = JsonConvert.SerializeObject(data, Formatting.None, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
        });

        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, json, Encoding.UTF8);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        File.Move(tmp, filePath);
    }

    public static ContinueSaveData Read(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = File.ReadAllText(filePath, Encoding.UTF8);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<ContinueSaveData>(json);
    }

    /// <summary>
    /// Enumerate continue save files, newest first. Files that fail to parse,
    /// have a wrong schema version, or come from a different mod/game version
    /// are filtered out. When <paramref name="requiredHostName"/> is non-empty,
    /// saves whose slot-0 player (the host of the saved run) is a different
    /// player are filtered out — the host has to remain the same across a
    /// continue, since the game's RUN save is host-authored and assumes the
    /// host's deck/relics/health sit in singletons. Other players can rotate
    /// freely; only host identity is locked.
    /// </summary>
    public static IReadOnlyList<ContinueSaveEntry> ListUsableSaves(int maxCount, string requiredModVersion, string requiredGameVersion, string requiredHostName = null)
    {
        var dir = ContinuesDirectory;
        if (!Directory.Exists(dir))
        {
            return Array.Empty<ContinueSaveEntry>();
        }

        var entries = new List<ContinueSaveEntry>();
        string[] files;
        try
        {
            files = Directory.GetFiles(dir, FILE_PREFIX + "*" + FILE_SUFFIX);
        }
        catch
        {
            return Array.Empty<ContinueSaveEntry>();
        }

        foreach (var file in files)
        {
            ContinueSaveData data;
            DateTime mtime;
            try
            {
                data = Read(file);
                mtime = File.GetLastWriteTimeUtc(file);
            }
            catch
            {
                continue;
            }

            if (data == null)
            {
                continue;
            }

            if (data.SchemaVersion != ContinueSaveData.SCHEMA_VERSION_CURRENT)
            {
                continue;
            }

            if (!string.Equals(data.ModVersion, requiredModVersion, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(data.GameVersion, requiredGameVersion, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(requiredHostName))
            {
                var savedHost = data.Players?.FirstOrDefault(p => p != null && p.SlotIndex == 0)?.PlayerName;
                if (!string.Equals(savedHost, requiredHostName, StringComparison.Ordinal))
                {
                    continue;
                }
            }

            entries.Add(new ContinueSaveEntry { FilePath = file, ModifiedUtc = mtime, Data = data });
        }

        entries.Sort((a, b) => b.ModifiedUtc.CompareTo(a.ModifiedUtc));

        if (maxCount > 0 && entries.Count > maxCount)
        {
            entries = entries.GetRange(0, maxCount);
        }

        return entries;
    }
}

public sealed class ContinueSaveEntry
{
    public string FilePath { get; set; }

    public DateTime ModifiedUtc { get; set; }

    public ContinueSaveData Data { get; set; }

    public string FileName => Path.GetFileName(FilePath);

    /// <summary>"VeretTV, sigseg1v" — display names from the save (not sanitized).</summary>
    public string DisplayPlayerNames
    {
        get
        {
            if (Data?.Players == null || Data.Players.Count == 0)
            {
                return string.Empty;
            }

            var names = new List<string>(Data.Players.Count);
            foreach (var p in Data.Players)
            {
                names.Add(string.IsNullOrEmpty(p.PlayerName) ? $"slot {p.SlotIndex}" : p.PlayerName);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", names);
        }
    }

    public string DisplayStageLabel => Data?.StageLabel ?? string.Empty;
}
