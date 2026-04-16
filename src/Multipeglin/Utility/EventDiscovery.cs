using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;

namespace Multipeglin.Utility;

/// <summary>
/// Scans game assemblies for all static delegate fields and compares
/// against what we've registered handlers for.
/// </summary>
public sealed class EventDiscovery
{
    private readonly ManualLogSource _log;

    /// <summary>
    /// All static delegate fields found in game assemblies.
    /// Key: "ClassName.FieldName", Value: the FieldInfo.
    /// </summary>
    public Dictionary<string, FieldInfo> AvailableEvents { get; } = new Dictionary<string, FieldInfo>();

    /// <summary>
    /// Event type IDs we have registered handlers for.
    /// </summary>
    public HashSet<string> RegisteredTypeIds { get; } = new HashSet<string>();

    public EventDiscovery(ManualLogSource log)
    {
        _log = log;
    }

    /// <summary>
    /// Scan the Assembly-CSharp assembly for all public static fields
    /// whose type derives from System.Delegate.
    /// </summary>
    public void ScanGameDelegates()
    {
        AvailableEvents.Clear();

        try
        {
            var asm = Assembly.Load("Assembly-CSharp");
            if (asm == null)
            {
                _log.LogWarning("Could not load Assembly-CSharp for event discovery");
                return;
            }

            foreach (var type in asm.GetTypes())
            {
                try
                {
                    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
                    foreach (var field in fields)
                    {
                        if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                        {
                            var key = $"{type.FullName}.{field.Name}";
                            AvailableEvents[key] = field;
                        }
                    }
                }
                catch
                {
                    // Skip types that fail to reflect (generic instantiation issues, etc.)
                }
            }

            _log.LogInfo($"Event discovery: found {AvailableEvents.Count} static delegate fields in game");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Event discovery scan failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Record that we have a handler registered for a given typeId.
    /// </summary>
    public void MarkRegistered(string typeId)
    {
        RegisteredTypeIds.Add(typeId);
    }

    /// <summary>
    /// Log the comparison between available game events and our registered handlers.
    /// </summary>
    public void LogReport()
    {
        _log.LogInfo("=== Event Discovery Report ===");
        _log.LogInfo($"Game delegates found: {AvailableEvents.Count}");
        _log.LogInfo($"Handlers registered:  {RegisteredTypeIds.Count}");

        // Group available events by declaring type for readability
        var grouped = AvailableEvents
            .GroupBy(kv => kv.Value.DeclaringType?.Name ?? "Unknown")
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var events = group.Select(kv => kv.Value.Name).OrderBy(n => n);
            _log.LogInfo($"  [{group.Key}] {string.Join(", ", events)}");
        }

        _log.LogInfo($"--- Registered handler typeIds ---");
        foreach (var id in RegisteredTypeIds.OrderBy(s => s))
        {
            _log.LogInfo($"  {id}");
        }
    }
}
