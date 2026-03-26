using System;
using System.IO;
using BepInEx.Logging;

namespace PeglinMods.Spectator.Utility;

public sealed class FileLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly string _filePath;

    public FileLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        _filePath = Path.Combine(logsDirectory, $"peglinmods_{timestamp}.log");
        _writer = new StreamWriter(_filePath, append: false) { AutoFlush = true };

        _writer.WriteLine($"PeglinMods log started at {DateTime.Now:O}");
        _writer.WriteLine(new string('-', 60));
    }

    public string FilePath => _filePath;

    public void Log(LogLevel level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
        _writer.WriteLine(line);
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
