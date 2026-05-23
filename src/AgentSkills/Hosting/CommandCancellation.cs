namespace AgentSkills.Hosting;

/// <summary>
/// Process-wide cancellation token tied to Ctrl-C. Registered as a singleton in DI
/// so any service that wants to cooperate with cancellation can inject it directly.
/// The first Ctrl-C requests cancellation cleanly; a second one lets the runtime
/// hard-exit (preserves the standard "two Ctrl-Cs to force quit" CLI behavior).
/// </summary>
public sealed class CommandCancellation : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private int _cancelCount;

    public CancellationToken Token => _cts.Token;

    public CommandCancellation()
    {
        Console.CancelKeyPress += OnCancel;
    }

    private void OnCancel(object? sender, ConsoleCancelEventArgs e)
    {
        if (Interlocked.Increment(ref _cancelCount) == 1)
        {
            e.Cancel = true;
            _cts.Cancel();
        }
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancel;
        _cts.Dispose();
    }
}
