using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Puppetty.Mcp;

/// <summary>
/// The MCP tool set: thin wrappers over <see cref="Terminal"/>. Every action waits for the screen to
/// settle and returns a screenshot, so the agent sees the result without another call.
/// </summary>
[McpServerToolType]
public sealed class TerminalTools(SessionManager sessions)
{
    private const string FormatDescription =
        "Screenshot format: 'text' (plain grid), 'annotated' (header with size, cursor and modes, styled spans such as reverse-video selections, numbered rows) or 'ansi' (with colour escapes).";

    [McpServerTool(Name = "terminal_launch"), Description(
        "Launch a program in a new pseudo-terminal and return its session id and first screen. " +
        "The session stays open until terminal_close.")]
    public Task<string> Launch(
        [Description("Program to run: a path or a name on PATH, e.g. 'vim' or 'python3'.")] string command,
        [Description("Arguments, one per element.")] string[]? args = null,
        [Description("Width in columns.")] int cols = 80,
        [Description("Height in rows.")] int rows = 24,
        [Description("Working directory. Defaults to the server's current directory.")] string? cwd = null,
        [Description("Extra environment variables. TERM defaults to xterm-256color.")] Dictionary<string, string>? env = null,
        [Description(FormatDescription)] string format = "annotated",
        CancellationToken cancellationToken = default) => Run(async () =>
    {
        var options = new LaunchOptions
        {
            Args = args ?? [],
            Cols = cols,
            Rows = rows,
            WorkingDirectory = cwd,
            Env = env ?? new Dictionary<string, string>(),
        };
        var session = await sessions.LaunchAsync(command, options, cancellationToken);
        var term = session.Terminal;

        // Give the program a moment to draw its first screen: wait for any output, then for it to settle.
        try { await term.WaitForScreenAsync(s => s.GetText().Trim().Length > 0, "first output", TimeSpan.FromSeconds(5), cancellationToken); }
        catch (TerminalTimeoutException) { }
        try { await term.WaitForIdleAsync(TimeSpan.FromSeconds(3), cancellationToken); }
        catch (TerminalTimeoutException) { }

        return $"session {session.Id} (pid {term.ProcessId})" + Exited(term) + "\n" + Shot(term, format);
    });

    [McpServerTool(Name = "terminal_type"), Description(
        "Type literal text into the session (a newline in the text is sent as Enter). Returns the screen after it settles.")]
    public Task<string> Type(
        [Description("Session id from terminal_launch.")] string session,
        [Description("Text to type.")] string text,
        [Description(FormatDescription)] string format = "annotated",
        CancellationToken cancellationToken = default) => Run(async () =>
    {
        var term = sessions.Get(session).Terminal;
        await term.TypeAsync(text, cancellationToken);
        return Shot(term, format);
    });

    [McpServerTool(Name = "terminal_press"), Description(
        "Press keys in order. Each element is a chord: 'Enter', 'Escape', 'Tab', 'Backspace', 'Up', 'Down', 'Left', 'Right', " +
        "'Home', 'End', 'PageUp', 'PageDown', 'Delete', 'F1'-'F12', or a single character, with optional 'Ctrl+', 'Alt+', 'Shift+' " +
        "prefixes, e.g. ['Ctrl+X', 'Ctrl+S']. Returns the screen after it settles.")]
    public Task<string> Press(
        [Description("Session id from terminal_launch.")] string session,
        [Description("Key chords to press in order.")] string[] keys,
        [Description(FormatDescription)] string format = "annotated",
        CancellationToken cancellationToken = default) => Run(async () =>
    {
        var term = sessions.Get(session).Terminal;
        await term.PressAsync(keys.Select(KeyChord.Parse), cancellationToken);
        return Shot(term, format);
    });

    [McpServerTool(Name = "terminal_paste"), Description(
        "Paste text as a terminal would (bracketed paste when the program enabled it). Returns the screen after it settles.")]
    public Task<string> Paste(
        [Description("Session id from terminal_launch.")] string session,
        [Description("Text to paste.")] string text,
        [Description(FormatDescription)] string format = "annotated",
        CancellationToken cancellationToken = default) => Run(async () =>
    {
        var term = sessions.Get(session).Terminal;
        await term.PasteAsync(text, cancellationToken);
        return Shot(term, format);
    });

    [McpServerTool(Name = "terminal_click"), Description(
        "Click a cell (zero-based row and column, as numbered in the annotated screenshot). Only programs that enabled " +
        "mouse reporting receive clicks; the annotated header shows 'mouse ...' when they have.")]
    public Task<string> Click(
        [Description("Session id from terminal_launch.")] string session,
        [Description("Zero-based row.")] int row,
        [Description("Zero-based column.")] int col,
        [Description("'left', 'middle' or 'right'.")] string button = "left",
        [Description(FormatDescription)] string format = "annotated",
        CancellationToken cancellationToken = default) => Run(async () =>
    {
        var term = sessions.Get(session).Terminal;
        var mouseButton = button.ToLowerInvariant() switch
        {
            "left" => MouseButton.Left,
            "middle" => MouseButton.Middle,
            "right" => MouseButton.Right,
            _ => throw new ArgumentException($"Unknown button '{button}'. Use left, middle or right."),
        };
        var sent = await term.ClickAsync(row, col, mouseButton, KeyModifiers.None, cancellationToken);
        var note = sent ? "" : "The program has not enabled mouse reporting; nothing was sent.\n";
        return note + Shot(term, format);
    });

    [McpServerTool(Name = "terminal_wait_for"), Description(
        "Wait until text appears, a regex matches a row, the screen goes idle, or the program exits. " +
        "Give exactly one of text, regex or until. Fails with the current screen on timeout.")]
    public Task<string> WaitFor(
        [Description("Session id from terminal_launch.")] string session,
        [Description("Text to wait for on a single row.")] string? text = null,
        [Description(".NET regular expression to wait for on a single row.")] string? regex = null,
        [Description("'idle' (screen stops changing) or 'exit' (program exits).")] string? until = null,
        [Description("Timeout in seconds.")] double timeoutSeconds = 10,
        [Description(FormatDescription)] string format = "annotated",
        CancellationToken cancellationToken = default) => Run(async () =>
    {
        var term = sessions.Get(session).Terminal;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var given = new[] { text, regex, until }.Count(x => x is not null);
        if (given != 1)
            throw new ArgumentException("Give exactly one of text, regex or until.");

        string result;
        if (text is not null)
        {
            await term.WaitForTextAsync(text, timeout, cancellationToken);
            result = $"found \"{text}\"";
        }
        else if (regex is not null)
        {
            var pattern = new Regex(regex);
            await term.WaitForScreenAsync(s => s.Contains(pattern), $"/{regex}/", timeout, cancellationToken);
            result = $"matched /{regex}/";
        }
        else if (until == "idle")
        {
            await term.WaitForIdleAsync(timeout, cancellationToken);
            result = "idle";
        }
        else if (until == "exit")
        {
            var code = await term.WaitForExitAsync(timeout, cancellationToken);
            result = $"exited with code {code}";
        }
        else
        {
            throw new ArgumentException($"Unknown until '{until}'. Use idle or exit.");
        }
        return result + "\n" + Shot(term, format);
    });

    [McpServerTool(Name = "terminal_screenshot"), Description("Return the current screen.")]
    public Task<string> Screenshot(
        [Description("Session id from terminal_launch.")] string session,
        [Description(FormatDescription)] string format = "text") => Run(() =>
    {
        var term = sessions.Get(session).Terminal;
        return Task.FromResult(Exited(term).TrimStart() is { Length: > 0 } e ? e + "\n" + Shot(term, format) : Shot(term, format));
    });

    [McpServerTool(Name = "terminal_read_region"), Description(
        "Return the text of a rectangle of the screen (zero-based), rows joined with newlines.")]
    public Task<string> ReadRegion(
        [Description("Session id from terminal_launch.")] string session,
        [Description("Top row.")] int row,
        [Description("Left column.")] int col,
        [Description("Width in columns.")] int width,
        [Description("Height in rows.")] int height) => Run(() =>
        Task.FromResult(sessions.Get(session).Terminal.Screen.GetText(row, col, width, height)));

    [McpServerTool(Name = "terminal_scrollback"), Description(
        "Return lines that have scrolled off the top of the screen, oldest first. Empty while a full-screen app uses the alternate screen.")]
    public Task<string> Scrollback(
        [Description("Session id from terminal_launch.")] string session,
        [Description("Return at most this many of the most recent lines.")] int lines = 200) => Run(() =>
    {
        var scrollback = sessions.Get(session).Terminal.GetScrollback(lines);
        return Task.FromResult(scrollback.Count == 0 ? "(no scrollback)" : string.Join('\n', scrollback));
    });

    [McpServerTool(Name = "terminal_resize"), Description("Resize the terminal; the program is told the new size. Returns the screen after it settles.")]
    public Task<string> Resize(
        [Description("Session id from terminal_launch.")] string session,
        [Description("New width in columns.")] int cols,
        [Description("New height in rows.")] int rows,
        [Description(FormatDescription)] string format = "annotated",
        CancellationToken cancellationToken = default) => Run(async () =>
    {
        var term = sessions.Get(session).Terminal;
        await term.ResizeAsync(cols, rows, cancellationToken);
        return Shot(term, format);
    });

    [McpServerTool(Name = "terminal_list"), Description("List open sessions.")]
    public Task<string> List() => Run(() =>
    {
        var all = sessions.List();
        if (all.Count == 0)
            return Task.FromResult("No sessions are open.");
        var sb = new StringBuilder();
        foreach (var s in all)
        {
            var screen = s.Terminal.Screen;
            sb.Append($"{s.Id}  {screen.Cols}x{screen.Rows}  {(s.Terminal.HasExited ? $"exited ({s.Terminal.ExitCode})" : $"running (pid {s.Terminal.ProcessId})")}  {s.Command}\n");
        }
        return Task.FromResult(sb.ToString());
    });

    [McpServerTool(Name = "terminal_close"), Description("End the session's program (killing it if still running) and close the session.")]
    public Task<string> Close([Description("Session id from terminal_launch.")] string session) => Run(async () =>
    {
        var code = await sessions.CloseAsync(session);
        return code is { } c ? $"closed {session}; it had exited with code {c}" : $"closed {session}; the program was killed";
    });

    private static string Shot(Terminal term, string format) => term.Screen.Render(format.ToLowerInvariant() switch
    {
        "text" => ScreenFormat.Text,
        "annotated" => ScreenFormat.Annotated,
        "ansi" => ScreenFormat.Ansi,
        _ => throw new ArgumentException($"Unknown format '{format}'. Use text, annotated or ansi."),
    });

    private static string Exited(Terminal term) => term.HasExited ? $" (exited with code {term.ExitCode})" : "";

    /// <summary>
    /// Turns expected failures into MCP errors with their message: the SDK hides the message of any
    /// other exception, and the agent needs it (a timeout carries the screen).
    /// </summary>
    private static async Task<string> Run(Func<Task<string>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is TerminalTimeoutException or KeyNotFoundException or ArgumentException or FormatException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}
