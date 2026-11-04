using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Puppetty.Cli;

/// <summary>
/// Background process that owns the sessions, so they outlive each <c>puppetty</c> invocation.
/// Started on demand by the client; exits after <see cref="IdleTimeout"/> with no sessions and no
/// requests, or on <c>puppetty stop</c>.
/// </summary>
internal sealed class Daemon
{
    private readonly SessionManager _sessions = new();
    private readonly CancellationTokenSource _stopping = new();
    private int _activeRequests;

    /// <summary>Idle time before exiting; <c>PUPPETTY_DAEMON_IDLE_SECONDS</c> overrides the 10-minute default.</summary>
    private static TimeSpan IdleTimeout =>
        double.TryParse(Environment.GetEnvironmentVariable("PUPPETTY_DAEMON_IDLE_SECONDS"), out var s) ? TimeSpan.FromSeconds(s) : TimeSpan.FromMinutes(10);

    public async Task<int> RunAsync()
    {
        // One daemon per pipe name. Two clients racing to auto-start could otherwise both succeed,
        // and on Windows both would serve the same pipe name with different sessions.
        using var single = new Mutex(initiallyOwned: false, $"puppetty-daemon-{Protocol.PipeName}");
        try
        {
            if (!single.WaitOne(0))
                return 0;
        }
        catch (AbandonedMutexException)
        {
            // A previous daemon died holding it; we own it now.
        }

        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var server = new NamedPipeServerStream(Protocol.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, Protocol.Options);
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                idle.CancelAfter(IdleTimeout);
                try
                {
                    await server.WaitForConnectionAsync(idle.Token);
                }
                catch (OperationCanceledException)
                {
                    await server.DisposeAsync();
                    if (_stopping.IsCancellationRequested || (_sessions.List().Count == 0 && Volatile.Read(ref _activeRequests) == 0))
                        break;
                    continue;
                }
                _ = HandleAsync(server);
            }
        }
        finally
        {
            await _sessions.DisposeAsync();
            single.ReleaseMutex();
        }
        return 0;
    }

    private async Task HandleAsync(NamedPipeServerStream pipe)
    {
        Interlocked.Increment(ref _activeRequests);
        try
        {
            await using (pipe)
            {
                using var reader = Protocol.Reader(pipe);
                await using var writer = Protocol.Writer(pipe);
                var request = await Protocol.ReadAsync<Request>(reader);
                if (request is null)
                    return;

                if (request.Op == "attach")
                {
                    await AttachAsync(request, pipe, writer);
                    return;
                }

                Response response;
                try
                {
                    response = Response.Success(await ExecuteAsync(request));
                }
                catch (Exception ex) when (ex is TerminalTimeoutException or KeyNotFoundException or ArgumentException or FormatException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
                {
                    response = Response.Failure(ex.Message);
                }
                await Protocol.WriteAsync(writer, response);
            }
        }
        catch (IOException)
        {
            // Client went away mid-request.
        }
        finally
        {
            Interlocked.Decrement(ref _activeRequests);
        }
    }

    private async Task<string> ExecuteAsync(Request r)
    {
        switch (r.Op)
        {
            case "start":
            {
                var session = await _sessions.LaunchAsync(Require(r.Command, "command"), new LaunchOptions
                {
                    Args = r.Args ?? [],
                    Cols = r.Cols ?? 80,
                    Rows = r.Rows ?? 24,
                    WorkingDirectory = r.Cwd,
                    Env = r.Env ?? new Dictionary<string, string>(),
                });
                // Let the program draw its first screen, so an immediate `snap` shows something.
                var term = session.Terminal;
                try { await term.WaitForScreenAsync(s => s.GetText().Trim().Length > 0, "first output", TimeSpan.FromSeconds(5)); }
                catch (TerminalTimeoutException) { }
                try { await term.WaitForIdleAsync(TimeSpan.FromSeconds(3)); }
                catch (TerminalTimeoutException) { }
                return session.Id;
            }
            case "type":
                await Term(r).TypeAsync(Require(r.Text, "text"));
                return "";
            case "press":
                await Term(r).PressAsync((r.Keys ?? []).Select(KeyChord.Parse));
                return "";
            case "paste":
                await Term(r).PasteAsync(Require(r.Text, "text"));
                return "";
            case "click":
            {
                var button = (r.Button ?? "left").ToLowerInvariant() switch
                {
                    "left" => MouseButton.Left,
                    "middle" => MouseButton.Middle,
                    "right" => MouseButton.Right,
                    var b => throw new ArgumentException($"Unknown button '{b}'. Use left, middle or right."),
                };
                var sent = await Term(r).ClickAsync(r.Row ?? 0, r.Col ?? 0, button);
                return sent ? "" : "The program has not enabled mouse reporting; nothing was sent.";
            }
            case "resize":
                await Term(r).ResizeAsync(r.Cols ?? throw new ArgumentException("cols is required"), r.Rows ?? throw new ArgumentException("rows is required"));
                return "";
            case "wait":
                return await WaitAsync(Term(r), r);
            case "snap":
                return Term(r).Screen.Render(ParseFormat(r.Format));
            case "scrollback":
                return string.Join('\n', Term(r).GetScrollback(r.Lines ?? int.MaxValue));
            case "ls":
            {
                var sb = new StringBuilder();
                foreach (var s in _sessions.List())
                {
                    var screen = s.Terminal.Screen;
                    var state = s.Terminal.HasExited ? $"exited ({s.Terminal.ExitCode})" : $"running (pid {s.Terminal.ProcessId})";
                    sb.Append($"{s.Id}\t{screen.Cols}x{screen.Rows}\t{state}\t{s.Command}\n");
                }
                return sb.ToString();
            }
            case "close":
            {
                var code = await _sessions.CloseAsync(Require(r.Id, "id"));
                return code?.ToString() ?? "killed";
            }
            case "stop":
                _stopping.Cancel();
                return "";
            default:
                throw new ArgumentException($"Unknown operation '{r.Op}'.");
        }
    }

    private static async Task<string> WaitAsync(Terminal term, Request r)
    {
        var timeout = TimeSpan.FromSeconds(r.TimeoutSeconds ?? 10);
        if (r.Text is not null)
        {
            await term.WaitForTextAsync(r.Text, timeout);
            return "";
        }
        if (r.Regex is not null)
        {
            var pattern = new Regex(r.Regex);
            await term.WaitForScreenAsync(s => s.Contains(pattern), $"/{r.Regex}/", timeout);
            return "";
        }
        switch (r.Until)
        {
            case "idle":
                await term.WaitForIdleAsync(timeout);
                return "";
            case "exit":
                return (await term.WaitForExitAsync(timeout)).ToString();
            default:
                throw new ArgumentException("Give one of --text, --regex, --idle or --exit.");
        }
    }

    /// <summary>Streams a frame now and after every change until the client leaves or the program's output ends.</summary>
    private async Task AttachAsync(Request r, Stream pipe, StreamWriter writer)
    {
        Terminal term;
        try
        {
            term = Term(r);
        }
        catch (KeyNotFoundException ex)
        {
            await Protocol.WriteAsync(writer, new Frame { Screen = ex.Message, Exited = true });
            return;
        }

        // Coalesce bursts of output into one pending frame.
        var changed = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        void OnChanged(object? sender, EventArgs e) => changed.Writer.TryWrite(true);
        term.Changed += OnChanged;

        // The client never writes after its request, so a completed read means it disconnected.
        using var gone = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await pipe.ReadAtLeastAsync(new byte[1], 1, throwOnEndOfStream: false); }
            catch (Exception) { }
            gone.Cancel();
        });

        Task SendAsync(bool exited)
        {
            var screen = term.Screen;
            return Protocol.WriteAsync(writer, new Frame
            {
                Screen = screen.Render(ScreenFormat.Ansi),
                CursorRow = screen.Cursor.Row,
                CursorCol = screen.Cursor.Col,
                CursorVisible = screen.CursorVisible,
                Exited = exited,
                ExitCode = term.ExitCode,
            }, gone.Token);
        }

        try
        {
            // Wait on exit as well as on changes: the output can end (the last change) before the
            // exit is reported, and then no further change would ever wake us.
            var exit = term.WaitForExitAsync(Timeout.InfiniteTimeSpan, gone.Token);
            while (!term.HasExited)
            {
                await SendAsync(exited: false);
                await Task.WhenAny(changed.Reader.ReadAsync(gone.Token).AsTask(), exit);
                gone.Token.ThrowIfCancellationRequested();
            }
            // Let the last output reach the screen, then send the final frame.
            try { await term.WaitForExitAsync(TimeSpan.FromSeconds(2), gone.Token); }
            catch (TerminalTimeoutException) { }
            await SendAsync(exited: true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            term.Changed -= OnChanged;
        }
    }

    private Terminal Term(Request r) => _sessions.Get(Require(r.Id, "id")).Terminal;

    private static string Require(string? value, string name) =>
        value ?? throw new ArgumentException($"{name} is required.");

    internal static ScreenFormat ParseFormat(string? format) => (format ?? "text").ToLowerInvariant() switch
    {
        "text" => ScreenFormat.Text,
        "annotated" => ScreenFormat.Annotated,
        "ansi" => ScreenFormat.Ansi,
        var f => throw new ArgumentException($"Unknown format '{f}'. Use text, annotated or ansi."),
    };
}
