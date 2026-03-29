using System;
using System.IO;
using BepInEx.Logging;

namespace PeglinMods.Multiplayer.Utility;

public sealed class FileLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly string _filePath;

    public FileLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);

        // Always use a shared log file. Both host and client append to the same file
        // with FileShare.ReadWrite so concurrent writers work.
        _filePath = Path.Combine(logsDirectory, "peglinmods_shared.log");
        var fs = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(fs) { AutoFlush = true };

        _writer.WriteLine($"--- PeglinMods log started at {DateTime.Now:O} ---");
    }

    public string FilePath => _filePath;

    /// <summary>
    /// Tag prepended to every log line once the role is known (HOST/CLIENT).
    /// </summary>
    public static string RoleTag { get; set; } = "";

    public void Log(LogLevel level, string message)
    {
        var tag = string.IsNullOrEmpty(RoleTag) ? "" : $"[{RoleTag}] ";
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {tag}{message}";
        try { _writer.WriteLine(line); } catch { }
    }

    public void Dispose()
    {
        _writer?.Dispose();
    }
}

public sealed class FileLogListener : ILogListener
{
    private readonly FileLogger _fileLogger;

    public FileLogListener(FileLogger fileLogger)
    {
        _fileLogger = fileLogger;
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if (eventArgs.Source?.SourceName?.StartsWith("PeglinMods") == true)
        {
            _fileLogger.Log(eventArgs.Level, $"[{eventArgs.Source.SourceName}] {eventArgs.Data}");
        }
    }

    public void Dispose() { }
}
