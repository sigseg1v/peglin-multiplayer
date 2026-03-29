using System;
using System.IO;
using BepInEx.Logging;
using NLog;
using NLog.Config;
using NLog.Targets;
using LogLevel = BepInEx.Logging.LogLevel;

namespace PeglinMods.Multiplayer.Utility;

public sealed class FileLogger : IDisposable
{
    private readonly NLog.Logger _nlog;
    private readonly string _filePath;

    public FileLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);
        _filePath = Path.Combine(logsDirectory, "peglinmods_shared.log");

        // Set initial instance tag from env var (e.g. PEGLIN1, PEGLIN2)
        var instance = Environment.GetEnvironmentVariable("PEGLINMODS_INSTANCE");
        if (!string.IsNullOrEmpty(instance))
            RoleTag = instance;

        // Configure NLog with concurrent file writes
        var config = new LoggingConfiguration();
        var fileTarget = new FileTarget("sharedLog")
        {
            FileName = _filePath,
            Layout = "${message}",        // We format lines ourselves
            KeepFileOpen = false,         // Close after each write for multi-process safety
            AutoFlush = true,
        };
        config.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, fileTarget);
        LogManager.Configuration = config;

        _nlog = LogManager.GetLogger("PeglinMods");
        _nlog.Info($"--- PeglinMods [{RoleTag}] log started at {DateTime.Now:O} ---");
    }

    public string FilePath => _filePath;

    /// <summary>
    /// Tag prepended to every log line. Initially set from PEGLINMODS_INSTANCE
    /// env var (e.g. "PEGLIN1"), then updated to "HOST" or "CLIENT" when role is chosen.
    /// </summary>
    public static string RoleTag { get; set; } = "";

    public void Log(LogLevel level, string message)
    {
        var tag = string.IsNullOrEmpty(RoleTag) ? "" : $"[{RoleTag}] ";
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {tag}{message}";
        _nlog.Info(line);
    }

    public void Dispose()
    {
        LogManager.Shutdown();
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
