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

Keys are encoded for the application's current modes (for example application cursor keys,
DECCKM). On Windows, keys go as win32-input-mode records when ConPTY asks for them.

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

## Assertions (`Puppetty.Testing`)

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
