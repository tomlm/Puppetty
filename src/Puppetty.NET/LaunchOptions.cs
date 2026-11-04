namespace Puppetty;

/// <summary>Options for <see cref="Terminal.LaunchAsync"/>.</summary>
public sealed class LaunchOptions
{
    /// <summary>Arguments passed to the program. Quoted for you on Windows.</summary>
    public IList<string> Args { get; init; } = [];

    /// <summary>Terminal width in columns. Default 80.</summary>
    public int Cols { get; init; } = 80;

    /// <summary>Terminal height in rows. Default 24.</summary>
    public int Rows { get; init; } = 24;

    /// <summary>Working directory. Defaults to the current directory.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Environment variables set on top of the current process's environment.
    /// <c>TERM</c> defaults to <c>xterm-256color</c>.
    /// </summary>
    public IDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();

    /// <summary>Lines of scrollback the emulator keeps. Default 1000.</summary>
    public int Scrollback { get; init; } = 1000;

    /// <summary>How actions decide the screen has settled.</summary>
    public SettleOptions Settle { get; init; } = new();

    /// <summary>Default timeout for the <c>WaitFor*</c> methods. Default 10 seconds.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Record a <see cref="Trace"/> of output, input, resizes and settling, for saving and replaying
    /// (the Expect assertions save it when one fails). Default true.
    /// </summary>
    public bool RecordTrace { get; init; } = true;

    /// <summary>Recording stops once the trace holds this many bytes of output and input. Default 16 MB.</summary>
    public long MaxTraceBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Clock used for settling and timeouts. Tests can substitute a fake.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>Timings for deciding the screen is "done" after an action.</summary>
public sealed class SettleOptions
{
    /// <summary>No output for this long counts as settled. Default 75 ms.</summary>
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromMilliseconds(75);

    /// <summary>
    /// Quiet period after a bare Escape, which applications hold back to tell apart from Alt+key.
    /// Default 250 ms.
    /// </summary>
    public TimeSpan EscapeQuietPeriod { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Longest an action waits to settle before returning anyway (for example, an app with a
    /// spinner never goes quiet). Default 5 seconds.
    /// </summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(5);
}
