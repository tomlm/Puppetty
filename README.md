# Puppetty

Playwright for terminals, in .NET. Run any CLI or TUI program in a real pseudo-terminal, emulate
its screen headlessly, and drive it from tests, scripts or AI agents: type, press keys, click,
wait for the screen, assert, snapshot and replay.

- **Emulator:** [XTerm.NET](https://www.nuget.org/packages/XTerm.NET), a headless xterm-compatible
  emulator.
- **PTY:** [Porta.Pty](https://www.nuget.org/packages/Porta.Pty). It uses the bundled ConPTY
  (OpenConsole) on Windows and openpty on Linux and macOS.
- **Target:** net10.0. Tested on Windows and Linux.

```csharp
using Puppetty;
using static Puppetty.Testing.Assertions;

await using var term = await Terminal.LaunchAsync("python3", new LaunchOptions { Args = ["-i", "-q"] });
await term.WaitForTextAsync(">>> ");
await term.TypeAsync("print(6*7)");
await term.PressAsync("Enter");
await Expect(term.GetByText("42")).ToBeVisibleAsync();
await Expect(term).ToMatchSnapshotAsync("python-42");
```

## Packages

| Project | What it is |
|---|---|
| `src/Puppetty` | Core: `Terminal`, `Screen`, locators, key/mouse/paste encoding, settling, snapshots, traces, sessions. |
| `src/Puppetty.Testing` | Auto-retrying `Expect(...)` assertions and golden snapshots; works with xUnit, NUnit and MSTest. |
| `src/Puppetty.Mcp` | MCP server (`puppetty-mcp`) giving AI agents the `terminal_*` tools. |
| `src/Puppetty.Cli` | `puppetty` command-line tool with daemon-backed sessions and a live `attach` view. |

## Docs

- [docs/api.md](docs/api.md): the library: launching, input, reading the screen, locators,
  assertions, settling, snapshots, traces.
- [docs/mcp.md](docs/mcp.md): the MCP server, setup in Claude Code, the annotated screenshot format.
- [docs/cli.md](docs/cli.md): the `puppetty` CLI and its daemon.
- [docs/conpty-quirks.md](docs/conpty-quirks.md): Windows ConPTY behaviour found while testing.
- [docs/step0-findings.md](docs/step0-findings.md): notes on XTerm.NET and Porta.Pty.

## Build and test

```sh
dotnet build Puppetty.slnx
dotnet test Puppetty.slnx
```

The end-to-end tests drive small fixture apps (`tests/fixtures/Puppetty.Fixture`). The smoke tests
drive real programs (vim, less, top/htop, pwsh, python) and skip any that aren't installed.
`samples/Puppetty.Samples.XUnit` is a small xUnit suite to copy from.
