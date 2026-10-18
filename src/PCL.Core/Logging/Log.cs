namespace PCL.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>极简线程安全日志：控制台 + 文件。</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _filePath;
    private static LogLevel _minLevel = LogLevel.Info;

    public static void Init(string? filePath = null, LogLevel minLevel = LogLevel.Info)
    {
        lock (Gate)
        {
            _filePath = filePath;
            _minLevel = minLevel;
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.AppendAllText(filePath, $"===== {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ====={Environment.NewLine}");
            }
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message, null);
    public static void Info(string message) => Write(LogLevel.Info, message, null);
    public static void Warn(string message, Exception? ex = null) => Write(LogLevel.Warn, message, ex);
    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    private static void Write(LogLevel level, string message, Exception? ex)
    {
        if (level < _minLevel) return;

        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant(),-5}] {message}";
        if (ex is not null) line += Environment.NewLine + ex;

        lock (Gate)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = level switch
            {
                LogLevel.Debug => ConsoleColor.DarkGray,
                LogLevel.Warn => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => old
            };
            Console.WriteLine(line);
            Console.ForegroundColor = old;

            if (!string.IsNullOrWhiteSpace(_filePath))
            {
                try
                {
                    File.AppendAllText(_filePath, line + Environment.NewLine);
                }
                catch
                {
                    // 日志失败不应影响主流程
                }
            }
        }
    }
}
