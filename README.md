[![Build and Test](https://github.com/tomlm/Puppetty/actions/workflows/BuildAndRunTests.yml/badge.svg)](https://github.com/tomlm/Puppetty/actions/workflows/BuildAndRunTests.yml)[![NuGet](https://img.shields.io/nuget/v/Puppetty.svg)](https://www.nuget.org/packages/Puppetty)

![Logo](https://raw.githubusercontent.com/tomlm/Puppetty/refs/heads/main/icon.png)

# Puppetty

Cross-platform Playwright for terminals, in .NET. Run any CLI or TUI program in a real pseudo-terminal, emulate
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

| Package | Install | What it is |
|---|---|---|
| **Puppetty.NET** (`src/Puppetty.NET`) | `dotnet add package Puppetty.NET` | The library, namespace `Puppetty`: `Terminal`, `Screen`, locators, key/mouse/paste encoding, settling, snapshots, traces. Also auto-retrying `Expect(...)` assertions and golden snapshots (namespace `Puppetty.Testing`) for xUnit, NUnit and MSTest. |
| **Puppetty** (`src/Puppetty`) | `dotnet tool install -g Puppetty` | The `puppetty` tool: shell commands with daemon-backed sessions and a live `attach` view, and `puppetty mcp`, an MCP server giving AI agents the `terminal_*` tools. |

## Docs

- [docs/api.md](docs/api.md): the library: launching, input, reading the screen, locators,
  assertions, settling, snapshots, traces.
  
- [docs/mcp.md](docs/mcp.md): the MCP server, setup in Claude Code, the annotated screenshot format.

- [docs/cli.md](docs/cli.md): the `puppetty` CLI and its daemon.

  

## Build and test

```sh
dotnet build Puppetty.slnx
dotnet test Puppetty.slnx
```

The end-to-end tests drive small fixture apps (`tests/fixtures/Puppetty.Fixture`). The smoke tests
drive real programs (vim, less, top/htop, pwsh, python) and skip any that aren't installed.
`samples/Puppetty.Samples.XUnit` is a small xUnit suite to copy from.
