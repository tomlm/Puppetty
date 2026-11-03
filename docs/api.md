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
```

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
