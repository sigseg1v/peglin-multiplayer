using System.Collections.Generic;

namespace Multipeglin.UI;

/// <summary>
/// Static ring buffer of received network events for the multiplayer feed display.
/// </summary>
public static class EventFeed
{
    private static readonly List<string> _entries = new List<string>();
    private const int MaxEntries = 200;
    private static int _version;

    public static int Version => _version;

    public static void Add(string typeId, string jsonPayload)
    {
        var line = $"<color=#88AAFF>[{typeId.Substring(typeId.LastIndexOf('.') + 1)}]</color> {jsonPayload}";
        lock (_entries)
        {
            _entries.Add(line);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
            }

            _version++;
        }
    }

    public static string GetText(int maxLines = 50)
    {
        lock (_entries)
        {
            var start = _entries.Count > maxLines ? _entries.Count - maxLines : 0;
            var sb = new System.Text.StringBuilder();
            for (var i = start; i < _entries.Count; i++)
            {
                sb.AppendLine(_entries[i]);
            }

            return sb.ToString();
        }
    }

    public static void Clear()
    {
        lock (_entries)
        { _entries.Clear();
            _version++; }
    }
}
