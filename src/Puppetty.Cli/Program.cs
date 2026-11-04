using System.CommandLine;
using System.Globalization;
using Puppetty.Cli;

var idOption = new Option<string>("--id") { Description = "Session id printed by `puppetty start`.", Required = true };

var root = new RootCommand("Drive CLI/TUI programs in a real terminal. Sessions are kept alive by a background daemon that starts on first use.");

// start
{
    var cmd = new Option<string?>("--cmd") { Description = "Command line to run, e.g. \"python3 -i -q\". Or pass the command after --." };
    var command = new Argument<string[]>("command") { Description = "Program and arguments (after --).", Arity = ArgumentArity.ZeroOrMore };
    var cols = new Option<int>("--cols") { Description = "Width in columns.", DefaultValueFactory = _ => 80 };
    var rows = new Option<int>("--rows") { Description = "Height in rows.", DefaultValueFactory = _ => 24 };
    var cwd = new Option<string?>("--cwd") { Description = "Working directory." };
    var env = new Option<string[]>("--env") { Description = "Environment variable KEY=VALUE; repeatable.", AllowMultipleArgumentsPerToken = false };
    var start = new Command("start", "Launch a program in a new session and print its id.") { cmd, command, cols, rows, cwd, env };
    start.SetAction((p, ct) =>
    {
        var words = p.GetValue(cmd) is { } line ? CommandSplitter.Split(line) : [.. p.GetValue(command) ?? []];
        if (words.Count == 0)
            throw new ArgumentException("Give a command with --cmd \"...\" or after --.");
        var variables = new Dictionary<string, string>();
        foreach (var pair in p.GetValue(env) ?? [])
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
                throw new ArgumentException($"--env expects KEY=VALUE, got '{pair}'.");
            variables[pair[..eq]] = pair[(eq + 1)..];
        }
        return Send(new Request
        {
            Op = "start", Command = words[0], Args = [.. words.Skip(1)],
            Cols = p.GetValue(cols), Rows = p.GetValue(rows), Cwd = p.GetValue(cwd) ?? Environment.CurrentDirectory, Env = variables,
        }, ct);
    });
    root.Subcommands.Add(start);
}

// type / paste
foreach (var (name, help) in new[] { ("type", "Type literal text (a newline is sent as Enter)."), ("paste", "Paste text (bracketed when the program enabled it).") })
{
    var text = new Argument<string>("text");
    var c = new Command(name, help) { idOption, text };
    c.SetAction((p, ct) => Send(new Request { Op = name, Id = p.GetValue(idOption), Text = p.GetValue(text) }, ct));
    root.Subcommands.Add(c);
}

// press
{
    var keys = new Argument<string[]>("keys") { Description = "Key chords in order: Enter, Escape, Up, F5, Ctrl+C, Alt+F, a, ...", Arity = ArgumentArity.OneOrMore };
    var press = new Command("press", "Press keys in order.") { idOption, keys };
    press.SetAction((p, ct) => Send(new Request { Op = "press", Id = p.GetValue(idOption), Keys = p.GetValue(keys) }, ct));
    root.Subcommands.Add(press);
}

// click
{
    var row = new Option<int>("--row") { Description = "Zero-based row.", Required = true };
    var col = new Option<int>("--col") { Description = "Zero-based column.", Required = true };
    var button = new Option<string>("--button") { Description = "left, middle or right.", DefaultValueFactory = _ => "left" };
    var click = new Command("click", "Click a cell (only delivered if the program enabled mouse reporting).") { idOption, row, col, button };
    click.SetAction((p, ct) => Send(new Request { Op = "click", Id = p.GetValue(idOption), Row = p.GetValue(row), Col = p.GetValue(col), Button = p.GetValue(button) }, ct));
    root.Subcommands.Add(click);
}

// resize
{
    var cols = new Option<int>("--cols") { Required = true };
    var rows = new Option<int>("--rows") { Required = true };
    var resize = new Command("resize", "Resize the terminal.") { idOption, cols, rows };
    resize.SetAction((p, ct) => Send(new Request { Op = "resize", Id = p.GetValue(idOption), Cols = p.GetValue(cols), Rows = p.GetValue(rows) }, ct));
    root.Subcommands.Add(resize);
}

// wait
{
    var text = new Option<string?>("--text") { Description = "Wait for this text on a single row." };
    var regex = new Option<string?>("--regex") { Description = "Wait for a row matching this .NET regex." };
    var idle = new Option<bool>("--idle") { Description = "Wait for the screen to stop changing." };
    var exit = new Option<bool>("--exit") { Description = "Wait for the program to exit; prints its exit code." };
    var timeout = new Option<string>("--timeout") { Description = "e.g. 5s, 500ms, 2m (default 10s).", DefaultValueFactory = _ => "10s" };
    var wait = new Command("wait", "Wait for text, a regex, idle or exit. Exits 1 with the screen on timeout.") { idOption, text, regex, idle, exit, timeout };
    wait.SetAction((p, ct) => Send(new Request
    {
        Op = "wait", Id = p.GetValue(idOption), Text = p.GetValue(text), Regex = p.GetValue(regex),
        Until = p.GetValue(exit) ? "exit" : p.GetValue(idle) ? "idle" : null,
        TimeoutSeconds = ParseDuration(p.GetValue(timeout)!).TotalSeconds,
    }, ct));
    root.Subcommands.Add(wait);
}

// snap
{
    var format = new Option<string>("--format") { Description = "text, annotated or ansi.", DefaultValueFactory = _ => "text" };
    var snap = new Command("snap", "Print the screen.") { idOption, format };
    snap.SetAction((p, ct) => Send(new Request { Op = "snap", Id = p.GetValue(idOption), Format = p.GetValue(format) }, ct));
    root.Subcommands.Add(snap);
}

// scrollback
{
    var lines = new Option<int?>("--lines") { Description = "At most this many of the most recent lines." };
    var scrollback = new Command("scrollback", "Print lines that scrolled off the top.") { idOption, lines };
    scrollback.SetAction((p, ct) => Send(new Request { Op = "scrollback", Id = p.GetValue(idOption), Lines = p.GetValue(lines) }, ct));
    root.Subcommands.Add(scrollback);
}

// ls / close / stop
{
    var ls = new Command("ls", "List sessions.");
    ls.SetAction((p, ct) => Send(new Request { Op = "ls" }, ct, startIfNeeded: false));
    root.Subcommands.Add(ls);

    var close = new Command("close", "End a session's program and close the session; prints its exit code or 'killed'.") { idOption };
    close.SetAction((p, ct) => Send(new Request { Op = "close", Id = p.GetValue(idOption) }, ct));
    root.Subcommands.Add(close);

    var stop = new Command("stop", "Close every session and stop the daemon.");
    stop.SetAction((p, ct) => Send(new Request { Op = "stop" }, ct, startIfNeeded: false));
    root.Subcommands.Add(stop);
}

// attach
{
    var attach = new Command("attach", "Live, read-only view of a session. Ctrl+C detaches.") { idOption };
    attach.SetAction((p, ct) => Attach.RunAsync(p.GetValue(idOption)!, ct));
    root.Subcommands.Add(attach);
}

// daemon (started automatically)
{
    var daemon = new Command("daemon", "Run the session daemon in the foreground.") { Hidden = true };
    daemon.SetAction((p, ct) => new Daemon().RunAsync());
    root.Subcommands.Add(daemon);
}

return await root.Parse(args).InvokeAsync();

static async Task<int> Send(Request request, CancellationToken cancellationToken, bool startIfNeeded = true)
{
    Response response;
    try
    {
        response = await DaemonClient.SendAsync(request, startIfNeeded, cancellationToken);
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

static TimeSpan ParseDuration(string text)
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
        : throw new ArgumentException($"Cannot read duration '{text}'. Use e.g. 5s, 500ms or 2m.");
}
