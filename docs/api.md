# Puppetty API

Running notes with examples. Everything public has XML docs; this page shows how the pieces fit.

## Launch

```csharp
await using var term = await Terminal.LaunchAsync("python3", new LaunchOptions
{
    Args = ["-i", "-q"],
    Cols = 100, Rows = 30,
    Env = { ["PYTHONSTARTUP"] = "" },   // TERM defaults to xterm-256color
    WorkingDirectory = "/tmp",
});
```

`command` is a path or a name on `PATH`. Arguments are quoted for you on Windows.
Disposing the terminal kills the program (and its process tree on Windows).

## Input

```csharp
await term.TypeAsync("print(6*7)");    // literal text; "\n" is sent as Enter
await term.PressAsync("Enter");
await term.PressAsync("Ctrl+C");
await term.PressAsync(Key.Down);
await term.PressAsync(Key.Tab, KeyModifiers.Shift);
await term.PressAsync([KeyChord.Parse("Ctrl+X"), KeyChord.Parse("Ctrl+S")]);
```

Chords: `Enter`, `Tab`, `Backspace`, `Escape`/`Esc`, `Space`, `Up`/`Down`/`Left`/`Right`,
`Home`, `End`, `PageUp`, `PageDown`, `Insert`, `Delete`, `F1`–`F12`, or any single character,
with `Ctrl+`, `Alt+`, `Shift+` prefixes. Case-insensitive.

Keys are encoded for the application's current modes, in this order of precedence:
1. the Kitty keyboard protocol (CSI-u), once the application has negotiated it;
2. on Windows, win32-input-mode records, which ConPTY asks for at startup (a bare Escape always goes this way);
3. otherwise legacy VT, honouring application cursor keys (DECCKM) and keypad mode.

```csharp
await term.PasteAsync("multi\nline");       // bracketed (ESC[200~ … ESC[201~) when the app enabled mode 2004
bool sent = await term.ClickAsync(row: 3, col: 10);           // press + release, in the app's mouse encoding
await term.ClickAsync(3, 10, MouseButton.Right, KeyModifiers.Control);
await term.ScrollWheelAsync(row: 5, col: 0, clicks: 3);        // positive = down
await term.GetByText("OK").ClickAsync();                       // waits for the text, clicks its first cell
await term.ResizeAsync(120, 40);                                // the app gets SIGWINCH
IReadOnlyList<string> old = term.GetScrollback(maxLines: 100);  // oldest first; empty on the alternate screen
```

`ClickAsync` and `ScrollWheelAsync` return false and send nothing when the application has not
enabled mouse reporting (`Screen.Modes.Mouse == MouseTracking.None`).

Every action returns once the screen has settled (see below).

## Reading the screen

```csharp
Screen s = term.Screen;          // immutable snapshot
string all = s.GetText();        // rows joined with \n, trailing spaces trimmed
string row0 = s.Line(0);
(int row, int col) = s.Cursor;
bool alt = s.IsAlternateScreen;
bool has = s.Contains(">>> ");   // matches full-width rows, so trailing spaces count
string box = s.GetText(row: 2, col: 4, width: 20, height: 3);

Cell c = s[0, 0];
// c.Text ("" for the right half of a wide character), c.Width (1, 2 or 0),
// c.Foreground / c.Background (Color.Default, Color.Palette(n), Color.Rgb(r, g, b)),
// c.Attributes (Bold, Dim, Italic, Underline, Blink, Inverse, Invisible, Strikethrough, Overline)
bool red = c.Foreground == AnsiColor.Red;   // AnsiColor converts to its palette colour
```

Colours are reported as the application set them, not resolved through a theme; reverse video
is an attribute and does not swap them.

```csharp
TerminalModes m = s.Modes;     // ApplicationCursorKeys, BracketedPaste, Mouse, MouseEncoding,
                               // FocusReporting, SynchronizedOutput, KittyKeyboardFlags, ...
string title = s.Title;
string forAgents = s.Render(ScreenFormat.Annotated);   // header + styled spans + numbered rows
string forHumans = s.Render(ScreenFormat.Ansi);        // with SGR colour escapes
```

The annotated format is described in [mcp.md](mcp.md#the-annotated-format).

## Locators

Locators are lazy: they are re-evaluated against the current screen on every use.

```csharp
Locator ok    = term.GetByText("OK");                      // every occurrence, on single rows
Locator total = term.GetByText(new Regex(@"total \d+"));
Locator box   = term.Region(row: 0, col: 0, width: 40, height: 1);
Locator title = term.Line(0);
CellLocator corner = term.Cell(0, 0);

bool visible = ok.IsVisible;          // matches right now?
int count = ok.Count;
string? text = total.Text;            // first match's text
ScreenMatch m = await ok.WaitForAsync();   // m.Row, m.Col (screen columns, wide-char aware), m.Text
IReadOnlyList<ScreenMatch> all = ok.Resolve(term.Screen);
```

A region or line "matches" when it holds any non-space text; its text is its rows joined with
`\n`, trailing spaces trimmed.

## Assertions (namespace `Puppetty.Testing`)

```csharp
using static Puppetty.Testing.Assertions;

await Expect(term.GetByText("Ready")).ToBeVisibleAsync();
await Expect(term.GetByText("Loading")).Not.ToBeVisibleAsync();
await Expect(term.GetByText(">")).ToHaveCountAsync(1);
await Expect(term.Line(0)).ToHaveTextAsync("Pick a fruit:");
await Expect(term.Line(0)).ToHaveTextAsync(new Regex("^Pick"));
await Expect(term.Line(3)).ToContainTextAsync("ok");

await Expect(term.Cell(0, 0)).ToHaveForegroundAsync(AnsiColor.Red);
await Expect(term.Cell(0, 0)).ToHaveBackgroundAsync(Color.Palette(200));
await Expect(term.Cell(1, 0)).ToHaveAttributesAsync(CellAttributes.Bold | CellAttributes.Underline);
await Expect(term.Cell(0, 0)).ToHaveTextAsync("E");

await Expect(term).ToContainTextAsync("Selected: Banana");
await Expect(term).ToHaveCursorAtAsync(2, 0);
await Expect(term).ToBeOnAlternateScreenAsync();

await Expect(term.GetByText("slow"), TimeSpan.FromSeconds(30)).ToBeVisibleAsync();
Assertions.DefaultTimeout = TimeSpan.FromSeconds(10);   // default 5 s
```

Every assertion re-checks each time the screen changes until it passes or times out, then throws
`ExpectationException` with the expectation, what was actually there, and the screen. Any test
framework reports it as a failure. See `samples/Puppetty.Samples.XUnit` for an xUnit suite.

## Waiting

```csharp
await term.WaitForTextAsync(">>> ");                         // default timeout: LaunchOptions.DefaultTimeout (10 s)
await term.WaitForTextAsync("Ready", TimeSpan.FromSeconds(5));
await term.WaitForScreenAsync(s => s.Line(0).StartsWith("Pick"), "menu title");
await term.WaitForIdleAsync();
int code = await term.WaitForExitAsync();
```

Waits re-check the screen each time output arrives; nothing polls on a fixed interval.
On failure they throw `TerminalTimeoutException`, whose message and `Screen` property show the
screen at that moment. If the program's output ends before the text appears, the wait fails
immediately instead of running out the clock.

## Settling

An action counts as settled when:

1. the application is not inside a synchronized-output block (DEC mode 2026), and
2. nothing has happened for `SettleOptions.QuietPeriod` (default 75 ms), counting both output
   received and the input just sent. After a bare Escape the period is
   `SettleOptions.EscapeQuietPeriod` (default 250 ms), because applications hold Escape back
   to tell it apart from Alt+key.

Actions stop waiting after `SettleOptions.MaxWait` (default 5 s) and return anyway, so an app
with a spinner doesn't hang them. `WaitForIdleAsync` throws instead.

```csharp
new LaunchOptions
{
    Settle = new SettleOptions { QuietPeriod = TimeSpan.FromMilliseconds(150) },
    TimeProvider = fakeTimeProvider,   // for tests of your own
};
```

Each settle decision is recorded in the trace, e.g. `settled quiet (81 ms)`,
`settled quiet after a synchronized frame (512 ms)` or `settled gave up: output kept arriving (5000 ms)`.

## Snapshots

```csharp
await Expect(term).ToMatchSnapshotAsync("vim-insert");
await Expect(term).ToMatchSnapshotAsync("status", new ScreenDiffOptions { CompareStyles = false, CompareCursor = true });
```

- Golden files are `__snapshots__/<name>.puppetty-snap` next to the test source file, or in
  `Assertions.SnapshotDirectory`. Commit them.
- A missing golden is created from the settled screen and the assertion passes. Under CI (the `CI`
  environment variable is set) a missing golden fails instead.
- `PUPPETTY_UPDATE_SNAPSHOTS=1` rewrites goldens from the current screens.
- The assertion retries until the screen matches. On failure the message holds a side-by-side diff
  (`^` = text differs, `~` = style only), and the actual snapshot and the trace are written to
  `Assertions.ArtifactsDirectory`.

The file is a zip container: `manifest.json` (format version, size, cursor, modes, title),
`cells.bin` (binary cell array: text, width, colours, attributes) and `screen.txt` (for people).
It is laid out to move to the `.tug` format later.

```csharp
Snapshot.Save(term.Screen, "screen.puppetty-snap");
Screen s = Snapshot.Load("screen.puppetty-snap");
ScreenDiff diff = ScreenDiff.Compare(expected, actual);
if (!diff.IsMatch) Console.WriteLine(diff);    // side-by-side, changed cells marked
```

## Traces

Every terminal records a trace by default (`LaunchOptions.RecordTrace`, capped by
`MaxTraceBytes`, 16 MB). It holds the program's raw output, everything sent to it (keys, pastes,
mouse, replies to its queries), resizes and settle decisions, all timestamped.

```csharp
Trace trace = term.GetTrace();
trace.Save("run.puppetty-trace");            // zip: manifest.json, events.bin, timeline.txt

var loaded = Trace.Load("run.puppetty-trace");
Screen end = loaded.Replay();                // feed the output back through the emulator
Screen then = loaded.Replay(until: TimeSpan.FromSeconds(1.5));
Console.WriteLine(loaded.ToTimeline());      // one line per event, escapes shown as \e
```

When any `Expect` assertion fails, Puppetty saves the trace to
`Assertions.ArtifactsDirectory` (`PUPPETTY_ARTIFACTS`, else `./puppetty-artifacts`) and names the
file in the failure message.
