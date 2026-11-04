using System.Globalization;
using CShellNet;
using Puppetty;
using Puppetty.Cli;
using Puppetty.Mcp;

// `start` and `press` end in a Rest, which can only be read back from the result, so they are
// declared by name and dispatched below. Every other command binds typed values and runs inline.
var cli = await Cli.For(args)
    .Program("puppetty")
    .Description("Drive CLI/TUI programs in a real terminal. Sessions are kept alive by a background daemon that starts on first use.")
    .UsageWhenEmpty()
    .Command("start", "launch a program in a new session and print its id", c => c
        .Rest("command", "[NAME=VALUE ...] program [args ...]; everything from the first word on is passed through")
        .Option("cols", "width in columns (default 80)")
        .Option("rows", "height in rows (default 24)")
        .Option("cwd", "working directory (default: the current one)")
        .Example("puppetty start --cols:100 python3 -i -q", "a Python REPL, 100 columns wide")
        .Example("puppetty start PYTHONSTARTUP= python3", "with an environment variable set (empty here)"))
    .Command("type", "type literal text (a newline is sent as Enter)", c =>
    {
        c.Argument(out string id, "session id printed by `puppetty start`");
        c.Argument(out string text, "the text; put -- before text that starts with a dash");
        c.RunAsync(() => Send(new Request { Op = "type", Id = id, Text = text }));
    })
    .Command("paste", "paste text (bracketed when the program enabled it)", c =>
    {
        c.Argument(out string id, "session id");
        c.Argument(out string text, "the text");
        c.RunAsync(() => Send(new Request { Op = "paste", Id = id, Text = text }));
    })
    .Command("press", "press keys in order", c => c
        .Argument("id", "session id")
        .Rest("keys", "key chords: Enter, Escape, Up, F5, Ctrl+C, Alt+F, a, ...")
        .Example("puppetty press t1 Down Down Enter", "move down twice and choose"))
    .Command("click", "click a cell (only delivered if the program enabled mouse reporting)", c =>
    {
        c.Argument(out string id, "session id");
        c.Argument(out int row, "zero-based row");
        c.Argument(out int col, "zero-based column");
        c.Option(out MouseButton? button, "which button (default left)");
        c.RunAsync(() => Send(new Request { Op = "click", Id = id, Row = row, Col = col, Button = (button ?? MouseButton.Left).ToString().ToLowerInvariant() }));
    })
    .Command("resize", "resize the terminal", c =>
    {
        c.Argument(out string id, "session id");
        c.Argument(out int cols, "width in columns");
        c.Argument(out int rows, "height in rows");
        c.RunAsync(() => Send(new Request { Op = "resize", Id = id, Cols = cols, Rows = rows }));
    })
    .Command("wait", "wait for text, a regex, idle or exit; exits 1 with the screen on timeout", c =>
    {
        c.Argument(out string id, "session id");
        c.Option(out string? text, "text to wait for, on a single row");
        c.Option(out string? regex, "a .NET regex to wait for, on a single row");
        c.Switch(out bool idle, "wait for the screen to stop changing");
        c.Switch(out bool exit, "wait for the program to exit; prints its exit code");
        c.Option(out string? timeout, "how long to wait: 5s, 500ms, 2m (default 10s)");
        c.Example("puppetty wait t1 --text:\">>> \" --timeout:30s", "wait for the Python prompt");
        c.RunAsync(() => ParseDuration(timeout ?? "10s") is not { } limit
            ? Fail($"puppetty wait: cannot read --timeout:{timeout}; use e.g. 5s, 500ms or 2m.")
            : Send(new Request
            {
                Op = "wait", Id = id, Text = text, Regex = regex,
                Until = exit ? "exit" : idle ? "idle" : null,
                TimeoutSeconds = limit.TotalSeconds,
            }));
    })
    .Command("snap", "print the screen", c =>
    {
        c.Argument(out string id, "session id");
        c.Option(out ScreenFormat? format, "text, annotated or ansi (default text)");
        c.RunAsync(() => Send(new Request { Op = "snap", Id = id, Format = (format ?? ScreenFormat.Text).ToString().ToLowerInvariant() }));
    })
    .Command("scrollback", "print lines that scrolled off the top", c =>
    {
        c.Argument(out string id, "session id");
        c.Option(out int? lines, "at most this many of the most recent lines");
        c.RunAsync(() => Send(new Request { Op = "scrollback", Id = id, Lines = lines }));
    })
    .Command("ls", "list sessions", c => c.RunAsync(() => Send(new Request { Op = "ls" }, startIfNeeded: false)))
    .Command("close", "end a session's program and close the session; prints its exit code or 'killed'", c =>
    {
        c.Argument(out string id, "session id");
        c.RunAsync(() => Send(new Request { Op = "close", Id = id }));
    })
    .Command("attach", "live, read-only view of a session; Ctrl+C detaches", c =>
    {
        c.Argument(out string id, "session id");
        c.RunAsync(() => Attach.RunAsync(id));
    })
    .Command("stop", "close every session and stop the daemon", c => c.RunAsync(() => Send(new Request { Op = "stop" }, startIfNeeded: false)))
    .Command("mcp", "run the MCP server on stdin/stdout, for AI agents", c => c.RunAsync(async () =>
    {
        await McpServer.RunAsync();
        return 0;
    }))
    .Command("daemon", "run the session daemon in the foreground (started automatically)", c => c.RunAsync(() => new Daemon().RunAsync()))
    .ParseAsync();

return cli.HandlerRan ? cli.ExitCode : cli.Command switch
{
    "start" => await Start(cli),
    "press" => await Press(cli),
    _ => 0,
};

static Task<int> Start(CliResult cli)
{
    // Leading NAME=VALUE words set environment variables, as in a POSIX shell.
    var words = cli.Rest.ToList();
    var env = new Dictionary<string, string>();
    while (words.Count > 0 && words[0].IndexOf('=') is var eq and > 0 && words[0][..eq].All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
    {
        env[words[0][..eq]] = words[0][(eq + 1)..];
        words.RemoveAt(0);
    }
    if (words.Count == 0)
        return Fail("puppetty start: give the program to run, e.g. `puppetty start python3 -i`.");

    var cols = ParseInt(cli, "cols", 80);
    var rows = ParseInt(cli, "rows", 24);
    if (cols is null || rows is null)
        return Task.FromResult(1);

    return Send(new Request
    {
        Op = "start", Command = words[0], Args = [.. words.Skip(1)],
        Cols = cols, Rows = rows, Cwd = cli.Option("cwd") ?? Environment.CurrentDirectory, Env = env,
    });
}

static Task<int> Press(CliResult cli) =>
    cli.Rest.Count == 0
        ? Fail("puppetty press: give at least one key, e.g. `puppetty press t1 Enter`.")
        : Send(new Request { Op = "press", Id = cli.Argument("id"), Keys = [.. cli.Rest] });

static int? ParseInt(CliResult cli, string name, int fallback)
{
    var text = cli.Option(name);
    if (text is null)
        return fallback;
    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        return value;
    Console.Error.WriteLine($"puppetty start: --{name} expects a positive whole number, got '{text}'.");
    return null;
}

static Task<int> Fail(string message)
{
    Console.Error.WriteLine(message);
    return Task.FromResult(1);
}

static async Task<int> Send(Request request, bool startIfNeeded = true)
{
    Response response;
    try
    {
        response = await DaemonClient.SendAsync(request, startIfNeeded);
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    if (!response.Ok)
    {
        // `ls` and `stop` with no daemon running are not errors.
        if (!startIfNeeded && response.Error == "The puppetty daemon is not running.")
            return 0;
        Console.Error.WriteLine(response.Error);
        return 1;
    }
    if (!string.IsNullOrEmpty(response.Output))
    {
        Console.Write(response.Output);
        if (!response.Output.EndsWith('\n'))
            Console.WriteLine();
    }
    return 0;
}

static TimeSpan? ParseDuration(string text)
{
    text = text.Trim();
    (string number, double scale) = text switch
    {
        _ when text.EndsWith("ms", StringComparison.OrdinalIgnoreCase) => (text[..^2], 0.001),
        _ when text.EndsWith('s') => (text[..^1], 1),
        _ when text.EndsWith('m') => (text[..^1], 60),
        _ => (text, 1),
    };
    return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        ? TimeSpan.FromSeconds(value * scale)
        : null;
}
