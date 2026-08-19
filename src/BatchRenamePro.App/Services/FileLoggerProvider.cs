using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BatchRenamePro.App.Services;

/// <summary>Writes log lines to a daily file next to the application's other data.</summary>
/// <remarks>
/// A desktop application has nowhere to send logs: there is no console attached to a WinExe and no
/// operator watching a stream. When a user reports that a rename did something unexpected, the only
/// thing that can settle it is a file on their machine — which is what this writes, and what the
/// About page points at. It deliberately stays inside this project rather than pulling in a logging
/// framework: one file, appended to, trimmed by age, is the whole requirement.
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetentionDays = 14;

    private readonly Lock _gate = new();
    private readonly string _directory;
    private readonly LogLevel _minimum;
    private string _path = string.Empty;
    private DateOnly _day;

    /// <summary>Creates the provider.</summary>
    /// <param name="directory">Where the log files go.</param>
    /// <param name="minimum">The lowest level worth writing.</param>
    public FileLoggerProvider(string directory, LogLevel minimum = LogLevel.Information)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = directory;
        _minimum = minimum;

        Prune();
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
        // Every write opens, appends and closes, so there is nothing held open to release.
    }

    private void Write(LogLevel level, string category, string message, Exception? error)
    {
        if (level < _minimum) return;

        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
            .Append(" [").Append(Abbreviate(level)).Append("] ")
            .Append(Shorten(category)).Append(": ")
            .AppendLine(message);

        if (error is not null) line.AppendLine(error.ToString());

        lock (_gate)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (_path.Length == 0 || today != _day)
                {
                    Directory.CreateDirectory(_directory);
                    _path = Path.Combine(_directory, $"app-{today:yyyyMMdd}.log");
                    _day = today;
                }

                File.AppendAllText(_path, line.ToString(), Encoding.UTF8);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written must never take the application down with it.
                Debug.WriteLine(line.ToString());
            }
        }
    }

    // A log directory that grows forever is a bug report waiting to happen on a small disk.
    private void Prune()
    {
        try
        {
            if (!Directory.Exists(_directory)) return;

            var cutoff = DateTime.Now.AddDays(-RetentionDays);

            foreach (var file in Directory.EnumerateFiles(_directory, "app-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Housekeeping is best-effort.
        }
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "___"
    };

    // "BatchRenamePro.App.ViewModels.RenameViewModel" is noise; "RenameViewModel" is the signal.
    private static string Shorten(string category)
    {
        var last = category.LastIndexOf('.');
        return last >= 0 && last < category.Length - 1 ? category[(last + 1)..] : category;
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minimum;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel)) return;

            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
