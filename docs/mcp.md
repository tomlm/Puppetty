# Puppetty MCP server

`puppetty mcp` runs a stdio MCP server that lets an agent run CLI and TUI programs in a real
pseudo-terminal: launch, type, press keys, click, wait, and read the screen. It is part of the
`puppetty` tool (the `Puppetty` package).

## Set up in Claude Code

Once the tool is installed (`dotnet tool install -g Puppetty`):

```sh
claude mcp add puppetty -- puppetty mcp
```

Or from a clone of this repo:

```sh
dotnet build src/Puppetty -c Release
claude mcp add puppetty -- dotnet "<repo>/src/Puppetty/bin/Release/net10.0/Puppetty.dll" mcp
```

Add `--scope project` to share the server with everyone working in a repository (it is written
to `.mcp.json`). Check it with `claude mcp list`, or `/mcp` inside Claude Code.

Any other MCP client works the same way: the command is `puppetty mcp` (or
`dotnet <path>/Puppetty.dll mcp`), speaking MCP over stdin/stdout. Logs go to stderr. The server's
sessions belong to it and end when it exits; they are separate from the CLI daemon's.

## Tools

Every action waits for the screen to settle (see [api.md](api.md#settling)) and returns a
screenshot, so the agent sees the result without a second call. Actions default to the
`annotated` format; `terminal_screenshot` defaults to `text`.

| Tool | Arguments | Returns |
|---|---|---|
| `terminal_launch` | `command`, `args`, `cols`=80, `rows`=24, `cwd`, `env` | `session t1 (pid N)` + first screen |
| `terminal_type` | `session`, `text` (newline = Enter) | screen |
| `terminal_press` | `session`, `keys` e.g. `["Ctrl+X","Ctrl+S"]` | screen |
| `terminal_paste` | `session`, `text` (bracketed when the app enabled it) | screen |
| `terminal_click` | `session`, `row`, `col`, `button`=left | screen; says so when the app has no mouse reporting |
| `terminal_wait_for` | `session`, one of `text` / `regex` / `until` (`idle`, `exit`), `timeoutSeconds`=10 | what happened + screen; an error with the screen on timeout |
| `terminal_screenshot` | `session`, `format` (`text`, `annotated`, `ansi`) | screen |
| `terminal_read_region` | `session`, `row`, `col`, `width`, `height` | text of the rectangle |
| `terminal_scrollback` | `session`, `lines`=200 | lines scrolled off the top |
| `terminal_resize` | `session`, `cols`, `rows` | screen |
| `terminal_list` | — | open sessions |
| `terminal_close` | `session` | exit code, or that it was killed |

All positions are zero-based. Sessions live until `terminal_close` or until the server exits,
which kills anything still running.

## The annotated format

```
size 80x24 · cursor 0,14 · alt-screen · mouse pressrelease/sgr · bracketed-paste · title "vim"
styles: r0 0-4 fg=red; r23 0-79 inverse
 0│hello from mcp
 1│~
 ...
23│-- INSERT --
```

- **Header:** size, cursor (with `hidden` when the app hid it), then only the modes that are on:
  `alt-screen` (full-screen app), `mouse <events>/<encoding>` (clicks will be delivered),
  `bracketed-paste`, `app-cursor-keys`, `kitty-keys <flags>`, `mid-frame` (inside a
  synchronized-output frame), and the window title.
- **styles:** runs of cells with the same non-default style, as `r<row> <cols> <style>`. Reverse
  video usually marks a selection or cursor line; a full-width styled run is usually a status
  bar. Capped at 30 runs.
- **Grid:** each row prefixed with its number, trailing spaces trimmed. Blank rows at the bottom
  are collapsed into one line.

## Verified

`tests/Puppetty.Tests/McpServerTests.cs` starts the server over stdio with the MCP SDK client:

- it lists the tools
- it drives the menu fixture to an exit code
- it returns errors with their messages, including the screen on a timeout
- it edits text in `vim -u NONE` (insert, Escape, `dw`, `:q!`). This runs where vim is installed (Linux CI).
