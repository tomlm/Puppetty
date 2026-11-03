# Step 0 findings: XTerm.NET and Porta.Pty

Status: M0 (investigation only, no code). Both repos were read without modification.
Repos inspected: `S:\github\XTerm.NET` (v3.0.2), `S:\github\Porta.Pty` (v2.2.3, HEAD `f142dd5`),
plus `S:\github\Avalonia.Terminal` as a reference consumer.

## 1. Verdict

| | XTerm.NET | Porta.Pty |
|---|---|---|
| NuGet | `XTerm.NET` 3.0.2 | `Porta.Pty` 2.2.3 |
| TFM | `net10.0` | `net10.0` |
| Dependencies | **none** | `Microsoft.Windows.Console.ConPTY` (bundled `conpty.dll` + `OpenConsole.exe`); native shim `libporta_pty` for linux/osx x64+arm64 |
| Usable headless as-is | **Yes** | **Yes**, with workarounds (§2, §5) |

- **XTerm.NET is already a pure emulator** (parser, buffer, scrollback, reflow, modes,
  input encoders, Sixel/Kitty decoders) with zero UI dependencies. **No `XTerm.NET.Core`
  extraction is needed.** All Avalonia/Skia code lives in `Iciclecreek.Avalonia.Terminal`.
- **Puppetty must target `net10.0` only.** Both dependencies are net10.0-only, so the spec's
  net8.0 target isn't possible without multi-targeting upstream.
- **Prior art:** `XTerm.NET/tools/vttest/VtDrive/Program.cs` already wires Porta.Pty into a
  headless `Terminal` (reads into `lock(terminal) terminal.Write(span)`, `DataReceived` →
  UTF-8 → PTY, `Resized` → `connection.Resize`, polling settle). It's a useful reference but
  has a scrollback bug (see §3.3); nothing will be copied.
- **Local dev loop:** Avalonia.Terminal switches from PackageReference to ProjectReference
  (`../../../XTerm.NET/...`) when `$(SolutionName)` ends in `.Local`. Puppetty can use the
  same pattern so upstream fixes can be tested before they ship.

## 2. Porta.Pty

### 2.1 Spawn

```csharp
// src/Porta.Pty/PtyProvider.cs
public static Task<IPtyConnection> SpawnAsync(PtyOptions options, CancellationToken cancellationToken);
public static string PseudoConsoleImplementation { get; } // "posix" | "inbox" | "oob" | "oob-no-host"
```

`PtyOptions` (`src/Porta.Pty/PtyOptions.cs`):

| Member | Notes |
|---|---|
| `string App` | required; resolved against `Cwd` / `PATH` on Windows, passed to `execvp` on Unix (parent's `PATH`, not the merged env) |
| `string[] CommandLine` | arguments only |
| `bool VerbatimCommandLine` | Windows: join args with spaces, no quoting |
| `string Cwd` | required |
| `IDictionary<string,string> Environment` | merged over parent env; empty value = "remove" (broken on Unix, §5) |
| `int Cols`, `int Rows` | |
| `bool UseAsyncIo` | overlapped pipes (Windows) / poll loop + pidfd reaper (Unix, kernel ≥ 5.3); makes `ReadAsync` honour cancellation |
| `string? Name` | unused |

Gotchas:
- `SpawnAsync` is synchronous underneath (`Task.FromResult`) and ignores the token.
- It **mutates `options.Environment`** (replaces it with the merged dictionary). Clone first.
- Unix sets `TERM=xterm-256color` by default; Windows sets nothing.
- Unix spawn failures (`chdir`/`execvp`) surface as an exit code (errno), not an exception.
  Windows throws.

### 2.2 Connection

```csharp
// src/Porta.Pty/IPtyConnection.cs
public interface IPtyConnection : IDisposable {
  event EventHandler<PtyExitedEventArgs>? ProcessExited;   // e.ExitCode
  Stream ReaderStream { get; }
  Stream WriterStream { get; }                             // unbuffered, no Flush needed
  bool SupportsCancellableRead { get; }                    // true only with UseAsyncIo
  int Pid { get; }
  int ExitCode { get; }
  bool WaitForExit(int milliseconds);                      // sync only
  void Kill();
  void Resize(int cols, int rows);
}
```

### 2.3 Exit and EOF

- **Windows, default I/O:** the pseudoconsole stays open after the child exits, so
  `ReaderStream.Read` blocks until `Dispose()`. **With `UseAsyncIo=true`** the pseudoconsole
  is closed on exit, buffered output drains, then the read returns 0.
- **Unix:** the read returns 0 after exit (Linux EIO is mapped to 0).
- **Unix signal deaths report exit code 0** (`Unix/PtyConnection.cs:443`); the signal isn't exposed.
- `Kill()` on Windows kills only the direct child; the job object (kill-on-close) takes the
  tree down on `Dispose()`. On Linux `Kill()` sends only `SIGHUP`; on macOS SIGHUP, 100 ms, SIGKILL.

### 2.4 How Puppetty will use it

- Always `UseAsyncIo = true` (cancellable reads, reliable EOF on Windows).
- `WaitForExitAsync` = `TaskCompletionSource` completed from `ProcessExited`.
- Close = `Kill()` then `Dispose()`.
- Clone `PtyOptions.Environment` before spawning.
- Build the Windows command line ourselves (MSVCRT quoting) and pass it with
  `VerbatimCommandLine = true`.

## 3. XTerm.NET

### 3.1 Construct, feed, resize (`src/XTerm.NET/Terminal.cs`)

```csharp
public Terminal(TerminalOptions? options = null);   // options are cloned
public void Write(ReadOnlySpan<byte> data);         // UTF-8, stateful across calls: the PTY path
public void Write(string data);
public void Resize(int cols, int rows);
public int Cols { get; } public int Rows { get; }
```

- Parsing is **synchronous**: when `Write` returns, the buffer is up to date and every event
  has already fired on the calling thread. No internal timers or threads.
- Relevant `TerminalOptions`: `Cols`, `Rows`, `Scrollback`, `TermName`, `WindowOptions`
  (all XTWINOPS replies default to off; enable them so apps get answers),
  `KittyKeyboardEnabled`, `KittyGraphicsEnabled`, `SixelEnabled`.

### 3.2 Cell model

`BufferCell` (`Buffer/BufferCell.cs`), a 24-byte struct:

| Field | Meaning |
|---|---|
| `int CodePoint` | leading code point |
| `int ClusterId` | non-zero for multi-code-point graphemes (ZWJ, combining, flags) |
| `string Content` | whole grapheme (derived) |
| `int Width` | 1 normal, 2 wide, **0 = continuation cell** (skip) |
| `AttributeData Attributes` | colours + flags |

`AttributeData` (`Buffer/AttributeData.cs`):
- Colours: use **`GetFgColorKind()` / `GetBgColorKind()`** → `ColorMode { Palette256, RGB, Palette16, Default }`
  and `GetFgColor()` / `GetBgColor()` for the value (palette index or `0xRRGGBB`).
  `Get*ColorMode()` only distinguishes RGB vs not (the README describes it wrongly).
- Flags: `IsBold/IsDim/IsItalic/IsUnderline/IsBlink/IsInverse/IsInvisible/IsStrikethrough/IsOverline`.
- `GetUnderlineStyle()`, `TryGetUnderlineColor(...)`.
- Palette resolution: `terminal.Colors`.
- Hyperlinks (OSC 8) are per line: `line.Links`, `line.TryGetLinkAt(col, out link)`.

### 3.3 Reading the grid

- `terminal.Buffer` is the active buffer (normal or alternate).
- Screen row `r` = `terminal.Buffer.Lines[Buffer.YBase + r]`. Scrollback = rows `0..YBase-1`.
  The alternate buffer has no scrollback.
- `BufferLine`: `this[int col]` (copy), `GetWidth`, `GetCodePoint`, `TranslateToString(trimRight, startCol, endCol)`,
  `IsWrapped`, `Clone()`.
- **Gotcha:** `terminal.GetLine(i)` takes an *absolute* index, so `GetLine(0)` is the top of
  scrollback (VtDrive gets this wrong). `GetVisibleLines()` follows the user scroll position
  (`YDisp`), not the active screen.
- `RecycleScrolledLines` defaults to true: never hold `BufferLine` references across writes;
  clone into Puppetty's own `Screen` snapshot.

### 3.4 Cursor

`Buffer.X`, `Buffer.Y` (screen-relative), `terminal.CursorVisible` (mode 25),
`Options.CursorStyle`. No cursor-moved event.

### 3.5 Modes

| Mode | API |
|---|---|
| DECCKM (1) | `ApplicationCursorKeys` |
| DECKPAM / DECNKM | `ApplicationKeypad` |
| Bracketed paste (2004) | `BracketedPasteMode` |
| Mouse 9/1000/1002/1003 | `MouseTrackingMode` |
| Mouse encoding 1005/1006/1015/1016 | `MouseEncoding` |
| **Synchronized output (2026)** | `SynchronizedOutput` + **`SynchronizedOutputChanged`** event (real transitions only, raised inside `Write`) |
| Alt screen (47/1047/1049) | `IsAlternateBufferActive`, `ActiveBuffer`, `BufferChanged` event |
| Focus reporting (1004) | `SendFocusEvents` |
| Kitty keyboard | `KittyKeyboardState.Flags`, `KittyKeyboardActive` |
| win32-input-mode (9001) | `Win32InputMode` |

There is no public "query mode N"; read the properties.

### 3.6 Outbound data and events

- **`DataReceived`** (`DataEventArgs.Data`, a UTF-16 string) carries every reply to the app
  (DA, DSR/CPR, DECRQM, XTGETTCAP, OSC colour queries, …) **and** `Paste()` output. Puppetty
  UTF-8-encodes it and writes it to the PTY.
- Also useful: `TitleChanged`, `BellRang`, `Resized`, `OscReceived`.
- There is **no generic "changed" / "render" event** (`RenderEventArgs` exists but is never
  raised). "`Write` returned" is the change signal.

### 3.7 Input encoders to reuse

```csharp
string  GenerateKeyInput(Key key, KeyModifiers modifiers = None);   // honours DECCKM / keypad
string  GenerateCharInput(char c, KeyModifiers modifiers = None);
string? GenerateKittyKeyInput(KeyEvent ev, KittyKeyboardEventType t = Press);
string  GenerateMouseEvent(MouseButton b, int x, int y, MouseEventType t, KeyModifiers m = None); // "" if mode filters it
string  GenerateFocusEvent(bool focused);
void    Paste(string text);                                          // honours 2004, emits via DataReceived
static  Win32InputGenerator.GenerateKeyEvent(...)                    // win32-input-mode
```

`KeyModifiers` = `{ Shift, Alt, Control }` (no Meta). The mouse generator is stateful
(`MouseTracker`). **Puppetty's key-encoding class will be a chord parser ("Ctrl+X",
"Alt+F", "F5") that delegates to these**, not a reimplementation of VT encoding.

### 3.8 Threading

- **Not thread-safe.** No locks on `Write` or buffer paths.
- Puppetty model: one pump task reads the PTY and calls `Write` under a lock; `Screen`
  snapshots are cloned under the same lock; input encoding (which reads modes) also takes it.
- Never call `Write` from inside an XTerm.NET event handler.

## 4. Implications for settling

- `SynchronizedOutputChanged(Active: false)` fires synchronously inside `Write` at the exact
  frame end. Settled = **not inside a 2026 block AND no bytes for N ms** (default 75 ms; longer
  after a bare Escape).
- The quiescence clock is driven by the pump's "bytes arrived" timestamps through an injected
  `TimeProvider`, so settle logic is unit-testable with a fake clock.

## 5. Windows / ConPTY (seed for `docs/conpty-quirks.md`)

- Default implementation is the **bundled out-of-band ConPTY** (OpenConsole); falls back to
  in-box kernel32 ConPTY if the binaries are missing. The choice is process-wide only
  (`PORTAPTY_CONPTY=inbox|oob`).
- **Passthrough mode is not available:** `CreatePseudoConsole` is called with
  `dwFlags = 0` (`Windows/PseudoConsole.cs:366`). As a result conhost consumes or redraws
  APC (Kitty graphics), XTGETTCAP, DECRQSS, OSC 10/11/52, and blocks Kitty keyboard negotiation.
- **Startup DA1:** Porta.Pty pre-answers OpenConsole's DA1 and strips the first `ESC[c` from
  output. Puppetty must not answer it again.
- ConPTY emits `ESC[?9001h` (win32-input-mode), `ESC[?1004h`, `ESC[6n`, `ESC[1t` at startup.
  XTerm.NET tracks `Win32InputMode`, so the key encoder must either send win32-input-mode
  events (`Win32InputGenerator`) or plain VT. **To be verified empirically in M1.**
- Argument quoting doubles quotes (`""`) and drops empty args; Puppetty will quote itself.
- Without `UseAsyncIo`, reads never EOF after exit (§2.3).

## 6. Proposed upstream changes (proposals only; not made)

Porta.Pty:
1. `PtyOptions.ConPtyFlags` (passthrough, inherit cursor) passed to `CreatePseudoConsole`,
   plus a per-spawn choice of in-box vs out-of-band ConPTY.
2. Unix: expose the terminating signal (e.g. `ExitSignal`) instead of reporting exit code 0.
3. Linux `Kill()`: SIGHUP, grace period, SIGKILL (as on macOS), or a `Kill(int signal)` overload.
4. Unix: let empty env values reach the native `unsetenv` branch (currently a no-op).
5. Windows: MSVCRT-compatible argument escaping that keeps empty arguments.

XTerm.NET: nothing required. Nice to have: README fix for `Get*ColorMode` and
`BufferChangedEventArgs.Buffer`; a public `GetMode(int)`.

## 7. Open items for M1

- Restructure the repo to the spec layout (solution at root, `src/`, `tests/`, `docs/`).
  The existing scaffold uses NUnit; keep NUnit for `Puppetty.Tests`.
- Verify win32-input-mode behaviour on Windows with the menu fixture.
