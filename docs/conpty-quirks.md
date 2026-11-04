# Windows ConPTY quirks

Puppetty runs Windows programs through Porta.Pty, which uses the **bundled** ConPTY
(`conpty.dll` + `OpenConsole.exe`), not the one built into Windows. Each entry: what happens,
how Puppetty handles it, and how to see it.

## win32-input-mode is requested at startup

OpenConsole sends `CSI ?9001h` as soon as the pseudoconsole starts, asking the terminal to send
keys as win32 INPUT_RECORD sequences (`CSI Vk;Sc;Uc;Kd;Cs;Rc _`). XTerm.NET tracks it as
`Terminal.Win32InputMode`.

- **Puppetty:** while the mode is on, `PressAsync` sends key-down + key-up records
  (`KeyEncoder.EncodeWin32`). `TypeAsync` sends plain text, which ConPTY also accepts.
- **Repro:** launch any program, then check that the emulator's `Win32InputMode` is true before
  the program has printed anything.
- **Verified:** the menu fixture (`Console.ReadKey`) and `python -i` receive arrows, Enter and
  text correctly this way (`FixtureTests`, `PythonReplTests`).

## A bare Escape has no unambiguous VT spelling

ConPTY's VT input parser cannot tell a lone `ESC` byte from the start of a sequence, so it
holds it back. Iciclecreek.Avalonia.Terminal works around this by always sending Escape as a
win32 record on Windows, unless the Kitty keyboard protocol is active.

- **Puppetty:** does the same (`KeyEncoder.UseWin32Input`).
- **Repro:** to be added with the vim smoke test (M3).

## Synchronized output (DEC mode 2026) is relayed

The bundled OpenConsole passes `CSI ?2026h` / `CSI ?2026l` through, so settling can wait for the
end of a frame on Windows as on Linux.

- **Verified:** `SyncOutputTests` (the fixture pauses 500 ms mid-frame). With the 2026 check
  disabled, the test sees the half-drawn frame on Windows, so the mode is reaching the emulator.

## Startup DA1 is answered by Porta.Pty

OpenConsole sends `CSI c` (DA1) at startup and waits up to 3 s for a reply. Porta.Pty answers it
itself and removes that first `CSI c` from the output.

- **Puppetty:** must not answer it again. XTerm.NET never sees it, so nothing to do.

## Silent fallback to the built-in conhost

If `conpty.dll` cannot find `OpenConsole.exe` beside it, it falls back to the conhost built into
Windows without an error. That conhost swallows Kitty graphics, XTGETTCAP, DECRQSS and
OSC 10/11/52. Puppetty consumes Porta.Pty from NuGet, which stages OpenConsole correctly.

- **Guard:** `ConPtyTests.UsesBundledOpenConsole` asserts
  `PtyProvider.PseudoConsoleImplementation == "oob"` on Windows.

## Kitty keyboard protocol works through OpenConsole

The spec expected ConPTY to block Kitty keyboard negotiation. With the bundled OpenConsole it
does not: `CSI > 1 u` from the application reaches the emulator, and CSI-u input sent back
(`ESC[27u` for Escape, `ESC[99;5u` for Ctrl+C) reaches the application unchanged.

- **Puppetty:** once the protocol is negotiated, Kitty encoding takes precedence over
  win32-input-mode. An application that negotiated CSI-u reads VT input, not INPUT_RECORDs.
  Iciclecreek.Avalonia.Terminal gives win32-input-mode precedence unconditionally, which would
  send such an application legacy keys on Windows. That may be worth revisiting there.
- **Verified:** `InputModeTests.KeysUseKittyProtocolOnceNegotiated`. The test failed on Windows
  while win32-input-mode came first, with Escape arriving as a plain `\e`.

## One input write can arrive as several reads

A bracketed paste written in one call (`ESC[200~hi there ESC[201~`) reached the application on
Windows as two reads: `ESC[200~`, then `hi there ESC[201~`. On Linux it arrived as one. That's
normal for a byte stream; applications must not assume one write is one read.

- **Repro:** `InputModeTests.PasteIsBracketedWhenTheAppAsks` joins the raw fixture's "got:" lines
  for this reason.

## Mouse and bracketed-paste modes are relayed

`CSI ?1000h`, `CSI ?1006h` and `CSI ?2004h` from the application reach the emulator, and SGR
mouse reports and paste brackets sent back reach the application byte for byte (when it reads
with `ENABLE_VIRTUAL_TERMINAL_INPUT`).

- **Verified:** `InputModeTests` (click, locator click, bracketed and plain paste).

## Not ConPTY: .NET console apps on Unix

These are testing pitfalls on Linux, not ConPTY quirks; they're recorded here because they cost
time.

- **.NET resets the terminal:** it sets up the terminal on first use, puts its own termios back
  after a child process exits (so `stty raw` from a child doesn't stick), and switches canonical
  mode back on around each read of the console input stream. The raw-input fixture therefore
  calls `cfmakeraw`/`tcsetattr` itself and reads fd 0 with `read(2)`.
- **Application cursor keys:** .NET turns on DECCKM at startup, so arrow keys must be sent as
  `ESC O A`. Puppetty's key encoder follows the mode.
