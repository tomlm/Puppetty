using System.Text;
using Porta.Pty;
using Emulator = XTerm.Terminal;
using EmulatorOptions = XTerm.Options.TerminalOptions;

namespace Puppetty;

/// <summary>
/// A program running in a real pseudo-terminal, with its screen emulated headlessly.
/// Launch one with <see cref="LaunchAsync"/>, drive it with <see cref="TypeAsync"/> and
/// <see cref="PressAsync(string, CancellationToken)"/>, and read it with <see cref="Screen"/> and the
/// <c>WaitFor*</c> methods.
/// </summary>
/// <example>
/// <code>
/// await using var term = await Terminal.LaunchAsync("python3", new LaunchOptions { Args = ["-i", "-q"] });
/// await term.WaitForTextAsync(">>> ");
/// await term.TypeAsync("print(6*7)");
/// await term.PressAsync("Enter");
/// await term.WaitForTextAsync("42");
/// </code>
/// </example>
public sealed class Terminal : IAsyncDisposable
{
    private const int ReadBufferSize = 64 * 1024;

    private readonly IPtyConnection _pty;
    private readonly Emulator _emulator;
    private readonly LaunchOptions _options;
    private readonly TimeProvider _time;
    private readonly bool _conPty = OperatingSystem.IsWindows();

    // _gate guards the emulator, _quiet, _replies, _outputEnded and _changed. XTerm.NET is not
    // thread-safe: the pump writes to it while callers read snapshots and encode keys.
    private readonly object _gate = new();
    private readonly Quiescence _quiet;
    private readonly List<string> _replies = [];
    private TaskCompletionSource _changed = NewSignal();
    private bool _outputEnded;

    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private readonly CancellationTokenSource _disposing = new();
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pump;
    private int _disposed;

    private Terminal(IPtyConnection pty, LaunchOptions options)
    {
        _pty = pty;
        _options = options;
        _time = options.TimeProvider;
        _quiet = new Quiescence(options.Settle, _time.GetUtcNow());

        _emulator = new Emulator(new EmulatorOptions
        {
            Cols = options.Cols,
            Rows = options.Rows,
            Scrollback = options.Scrollback,
        });
        // Replies to the application's queries (DA, DSR, ...) are raised inside Write, under _gate.
        // Collect them there and send after the lock is released so a slow PTY never blocks readers.
        _emulator.DataReceived += (_, e) => _replies.Add(e.Data);

        _pty.ProcessExited += (_, e) => _exited.TrySetResult(e.ExitCode);
        // The child may have exited before the handler was attached.
        if (_pty.WaitForExit(0))
            _exited.TrySetResult(_pty.ExitCode);

        _pump = Task.Run(PumpAsync);
    }

    /// <summary>Launches <paramref name="command"/> in a new pseudo-terminal.</summary>
    /// <param name="command">Program to run: a path, or a name found on <c>PATH</c>.</param>
    /// <param name="options">Size, arguments, environment and timing options.</param>
    /// <param name="cancellationToken">Cancels the launch.</param>
    /// <returns>The running terminal. Dispose it to end the program.</returns>
    public static async Task<Terminal> LaunchAsync(string command, LaunchOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        options ??= new LaunchOptions();

        var env = new Dictionary<string, string>(options.Env);
        env.TryAdd("TERM", "xterm-256color");

        var ptyOptions = new PtyOptions
        {
            App = command,
            Cwd = options.WorkingDirectory ?? Environment.CurrentDirectory,
            Cols = options.Cols,
            Rows = options.Rows,
            Environment = env,
            UseAsyncIo = true,
        };
        if (OperatingSystem.IsWindows())
        {
            // Porta.Pty's own quoting drops empty arguments and escapes quotes non-standardly.
            ptyOptions.CommandLine = options.Args.Count == 0 ? [] : [WindowsCommandLine.Join(options.Args)];
            ptyOptions.VerbatimCommandLine = true;
        }
        else
        {
            ptyOptions.CommandLine = [.. options.Args];
        }

        var pty = await PtyProvider.SpawnAsync(ptyOptions, cancellationToken).ConfigureAwait(false);
        return new Terminal(pty, options);
    }

    /// <summary>A snapshot of the screen right now.</summary>
    public Screen Screen
    {
        get
        {
            lock (_gate)
                return Screen.Capture(_emulator);
        }
    }

    /// <summary>The process id of the launched program.</summary>
    public int ProcessId => _pty.Pid;

    /// <summary>Whether the program has exited.</summary>
    public bool HasExited => _exited.Task.IsCompleted;

    /// <summary>The program's exit code, or null while it is running.</summary>
    public int? ExitCode => _exited.Task.IsCompletedSuccessfully ? _exited.Task.Result : null;

    /// <summary>Locates every occurrence of <paramref name="text"/> on single rows.</summary>
    public Locator GetByText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TextLocator(this, text);
    }

    /// <summary>Locates every match of <paramref name="pattern"/> on single rows.</summary>
    public Locator GetByText(System.Text.RegularExpressions.Regex pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new TextLocator(this, pattern);
    }

    /// <summary>Locates a fixed rectangle. Its text is its rows joined with <c>\n</c>, trailing spaces trimmed.</summary>
    public Locator Region(int row, int col, int width, int height) => new RegionLocator(this, row, col, width, height);

    /// <summary>Locates one whole row.</summary>
    public Locator Line(int row) => new RegionLocator(this, row, 0, int.MaxValue, 1, $"line {row}");

    /// <summary>Locates one cell, for checking its colours and attributes.</summary>
    public CellLocator Cell(int row, int col) => new(this, row, col);

    /// <summary>Types <paramref name="text"/> as literal keystrokes, then waits for the screen to settle.</summary>
    /// <remarks><c>\n</c> is sent as Enter (<c>\r</c>), as a keyboard would.</remarks>
    public async Task TypeAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        await SendInputAsync(text.Replace("\r\n", "\r").Replace('\n', '\r'), bareEscape: false, cancellationToken).ConfigureAwait(false);
        await SettleAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Presses one or more key chords in order, such as <c>"Enter"</c>, <c>"Ctrl+C"</c>, <c>"Alt+F"</c>,
    /// <c>"Up"</c> or <c>"F5"</c>, then waits for the screen to settle.
    /// </summary>
    /// <exception cref="FormatException">A chord is not recognised.</exception>
    public Task PressAsync(string keys, CancellationToken cancellationToken = default) =>
        PressAsync([KeyChord.Parse(keys)], cancellationToken);

    /// <summary>Presses a named key with optional modifiers, then waits for the screen to settle.</summary>
    public Task PressAsync(Key key, KeyModifiers modifiers = KeyModifiers.None, CancellationToken cancellationToken = default) =>
        PressAsync([KeyChord.Of(key, modifiers)], cancellationToken);

    /// <summary>Presses each chord in order, then waits for the screen to settle.</summary>
    public async Task PressAsync(IEnumerable<KeyChord> chords, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chords);
        var list = chords.ToList();
        if (list.Count == 0)
            return;

        // Encoded together under one lock: the modes are read once, as a real terminal would at
        // the moment of the key press.
        string bytes;
        lock (_gate)
            bytes = string.Concat(list.Select(c => KeyEncoder.Encode(c, _emulator, _conPty)));

        var last = list[^1];
        var bareEscape = last.Key == Key.Escape && last.Modifiers == KeyModifiers.None;
        await SendInputAsync(bytes, bareEscape, cancellationToken).ConfigureAwait(false);
        await SettleAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until the screen settles: no synchronized-output block is open and no output has arrived
    /// for <see cref="SettleOptions.QuietPeriod"/>.
    /// </summary>
    /// <exception cref="TerminalTimeoutException">The screen kept changing for longer than <paramref name="timeout"/>.</exception>
    public async Task WaitForIdleAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (!await TrySettleAsync(timeout ?? _options.DefaultTimeout, cancellationToken).ConfigureAwait(false))
            throw new TerminalTimeoutException($"Screen did not settle within {timeout ?? _options.DefaultTimeout}.", Screen);
    }

    /// <summary>Waits until <paramref name="text"/> appears on a single row of the screen.</summary>
    /// <returns>The screen that contained the text.</returns>
    /// <exception cref="TerminalTimeoutException">It did not appear in time, or the program's output ended first.</exception>
    public Task<Screen> WaitForTextAsync(string text, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WaitForScreenAsync(s => s.Contains(text), $"text \"{text}\"", timeout, cancellationToken);
    }

    /// <summary>Waits until <paramref name="predicate"/> holds for the screen.</summary>
    /// <param name="predicate">Evaluated on the current screen and again after each change.</param>
    /// <param name="description">Names the condition in the timeout message.</param>
    /// <param name="timeout">Defaults to <see cref="LaunchOptions.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The screen that satisfied the predicate.</returns>
    /// <exception cref="TerminalTimeoutException">It did not hold in time, or the program's output ended first.</exception>
    public async Task<Screen> WaitForScreenAsync(Func<Screen, bool> predicate, string description = "condition", TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var limit = timeout ?? _options.DefaultTimeout;
        var deadline = _time.GetUtcNow() + limit;
        while (true)
        {
            Screen screen;
            Task changed;
            bool ended;
            lock (_gate)
            {
                screen = Screen.Capture(_emulator);
                changed = _changed.Task;
                ended = _outputEnded;
            }

            if (predicate(screen))
                return screen;
            if (ended)
                throw new TerminalTimeoutException($"Output ended (exit code {ExitCode?.ToString() ?? "unknown"}) before {description} appeared.", screen, outputEnded: true);

            var left = deadline - _time.GetUtcNow();
            if (left <= TimeSpan.Zero)
                throw new TerminalTimeoutException($"Timed out after {limit} waiting for {description}.", screen);
            await WhenChangedOrElapsed(changed, left, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits for the program to exit and for its remaining output to reach the screen.
    /// </summary>
    /// <returns>The exit code.</returns>
    /// <exception cref="TerminalTimeoutException">The program was still running after <paramref name="timeout"/>.</exception>
    public async Task<int> WaitForExitAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? _options.DefaultTimeout;
        var started = _time.GetUtcNow();
        try
        {
            await _exited.Task.WaitAsync(limit, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new TerminalTimeoutException($"Process {_pty.Pid} did not exit within {limit}.", Screen);
        }

        // Output written just before exit may still be in flight. The read ends at EOF once the
        // PTY closes; a grandchild holding the PTY open would keep it from closing, so don't wait
        // past the caller's timeout for that.
        var left = limit - (_time.GetUtcNow() - started);
        if (left > TimeSpan.Zero)
            await Task.WhenAny(_pump, Task.Delay(left, _time, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return _exited.Task.Result;
    }

    /// <summary>Ends the program (and, on Windows, its process tree) and releases the terminal.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _disposing.Cancel();
        if (!HasExited)
        {
            try { _pty.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { }
        }
        _pty.Dispose();
        try { await _pump.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _disposing.Dispose();
        _inputGate.Dispose();
    }

    private async Task PumpAsync()
    {
        var buffer = new byte[ReadBufferSize];
        try
        {
            while (true)
            {
                var read = await _pty.ReaderStream.ReadAsync(buffer, _disposing.Token).ConfigureAwait(false);
                if (read <= 0)
                    break;

                string? replies = null;
                TaskCompletionSource changed;
                lock (_gate)
                {
                    _emulator.Write(buffer.AsSpan(0, read));
                    _quiet.OnOutput(_time.GetUtcNow());
                    if (_replies.Count > 0)
                    {
                        replies = string.Concat(_replies);
                        _replies.Clear();
                    }
                    changed = _changed;
                    _changed = NewSignal();
                }
                changed.TrySetResult();

                if (replies is not null)
                    await WriteAsync(replies, _disposing.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Disposed, or the PTY closed under us: either way the output has ended.
        }
        finally
        {
            TaskCompletionSource changed;
            lock (_gate)
            {
                _outputEnded = true;
                changed = _changed;
            }
            changed.TrySetResult();
        }
    }

    private async Task SendInputAsync(string text, bool bareEscape, CancellationToken cancellationToken)
    {
        if (text.Length == 0)
            return;
        await WriteAsync(text, cancellationToken).ConfigureAwait(false);
        lock (_gate)
            _quiet.OnInput(_time.GetUtcNow(), bareEscape);
    }

    private async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _pty.WriterStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inputGate.Release();
        }
    }

    /// <summary>Settles after an action; gives up quietly after <see cref="SettleOptions.MaxWait"/>.</summary>
    private Task SettleAsync(CancellationToken cancellationToken) =>
        TrySettleAsync(_options.Settle.MaxWait, cancellationToken);

    private async Task<bool> TrySettleAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = _time.GetUtcNow() + timeout;
        while (true)
        {
            TimeSpan? remaining;
            Task changed;
            lock (_gate)
            {
                if (_outputEnded)
                    return true;
                remaining = _quiet.Remaining(_time.GetUtcNow(), _emulator.SynchronizedOutput);
                changed = _changed.Task;
            }
            if (remaining == TimeSpan.Zero)
                return true;

            var left = deadline - _time.GetUtcNow();
            if (left <= TimeSpan.Zero)
                return false;
            // Inside a synchronized-output block, wait for the next output (the block ending is
            // output); otherwise wait out the rest of the quiet period, cut short by new output.
            var wait = remaining is { } r && r < left ? r : left;
            await WhenChangedOrElapsed(changed, wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WhenChangedOrElapsed(Task changed, TimeSpan wait, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(wait, _time, cts.Token);
        await Task.WhenAny(changed, delay).ConfigureAwait(false);
        cts.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
