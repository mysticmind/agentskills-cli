using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AgentSkills.Hosting;

/// <summary>
/// Minimal <see cref="ILoggerProvider"/> that routes log records to the Spectre
/// console. Designed for CLI tools where Information / Warning / Error map to
/// colored prefixes and the user-facing tables are written separately via
/// <see cref="IAnsiConsole"/> directly. Trace and Debug only surface when
/// <c>--verbose</c> raises the minimum level.
/// </summary>
public sealed class SpectreLoggerProvider : ILoggerProvider
{
    private readonly IAnsiConsole _console;
    private readonly LogLevel _minimumLevel;

    public SpectreLoggerProvider(IAnsiConsole console, LogLevel minimumLevel)
    {
        ArgumentNullException.ThrowIfNull(console);
        _console = console;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) => new SpectreLogger(_console, _minimumLevel);

    public void Dispose() { /* nothing owned */ }
}

internal sealed class SpectreLogger(IAnsiConsole console, LogLevel minimumLevel) : ILogger
{
    private readonly IAnsiConsole _console = console;
    private readonly LogLevel _minimumLevel = minimumLevel;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null) return;

        var prefix = logLevel switch
        {
            LogLevel.Trace => "[grey]trace[/]",
            LogLevel.Debug => "[grey]debug[/]",
            LogLevel.Information => "[cyan]info[/] ",
            LogLevel.Warning => "[yellow]warn[/] ",
            LogLevel.Error => "[red]error[/]",
            LogLevel.Critical => "[red bold]crit[/] ",
            _ => string.Empty,
        };

        if (logLevel >= LogLevel.Warning)
        {
            _console.MarkupLine($"{prefix} {Markup.Escape(message)}");
        }
        else
        {
            _console.MarkupLine($"[grey]{prefix} {Markup.Escape(message)}[/]");
        }

        if (exception is not null && _minimumLevel <= LogLevel.Debug)
        {
            _console.WriteException(exception, ExceptionFormats.ShortenEverything);
        }
    }
}
