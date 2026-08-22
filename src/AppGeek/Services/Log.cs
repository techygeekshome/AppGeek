using System.Text;

namespace AppGeek.Services;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>Minimal thread-safe file logger. One file per day, plus one per install run.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _runFile;

    public static string DailyFile =>
        Path.Combine(AppPaths.LogDir, $"appgeek-{DateTime.Now:yyyyMMdd}.log");

    public static event Action<string, LogLevel>? LineWritten;

    public static string BeginRunLog()
    {
        lock (Gate)
        {
            _runFile = Path.Combine(AppPaths.LogDir, $"appgeek-run-{DateTime.Now:yyyyMMdd-HHmm}.log");
            return _runFile;
        }
    }

    public static void EndRunLog()
    {
        lock (Gate) { _runFile = null; }
    }

    public static void Debug(string m) => Write(m, LogLevel.Debug);
    public static void Info(string m) => Write(m, LogLevel.Info);
    public static void Warn(string m) => Write(m, LogLevel.Warn);
    public static void Error(string m, Exception? ex = null) =>
        Write(ex is null ? m : $"{m} :: {ex.GetType().Name}: {ex.Message}", LogLevel.Error);

    public static void Write(string message, LogLevel level = LogLevel.Info)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  [{level.ToString().ToUpperInvariant(),-5}]  {message}";
        lock (Gate)
        {
            TryAppend(DailyFile, line);
            if (_runFile is not null) TryAppend(_runFile, line);
        }
        LineWritten?.Invoke(line, level);
    }

    private static void TryAppend(string path, string line)
    {
        try { File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8); }
        catch { /* logging must never take the app down */ }
    }
}
