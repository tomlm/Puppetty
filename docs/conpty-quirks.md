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
