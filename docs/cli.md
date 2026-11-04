# `puppetty` CLI

Drive terminal programs from a shell or script. Sessions live in a background daemon, so each
command is a separate, short invocation. The same tool runs the MCP server (`puppetty mcp`, see
[mcp.md](mcp.md)).

```sh
dotnet tool install -g Puppetty            # or: dotnet run --project src/Puppetty -- <command>

id=$(puppetty start --cols:80 --rows:24 python3 -i -q)   # prints the session id, e.g. t1
puppetty type    $id "print(6*7)"
puppetty press   $id Enter
puppetty wait    $id --text:">>> " --timeout:5s
puppetty snap    $id                       # --format:text|annotated|ansi
puppetty ls
puppetty attach  $id                       # live read-only view; Ctrl+C detaches
puppetty close   $id                       # prints the exit code, or "killed"
```

`puppetty --help` lists the commands and `puppetty <command> --help` shows what each one takes.

## Command-line rules

Arguments are parsed by [CShell](https://www.nuget.org/packages/CShell)'s `Cli`:

- **Option values attach** with `:` or `=`: `--cols:100`, `--text=">>> "`. The separated form
  (`--cols 100`) is refused with an error rather than misread.
- **Switch names are forgiving:** `-idle`, `--idle` and `--Idle` are the same switch.
- **Anything unknown is an error,** including a misspelled switch or an enum value that doesn't
  exist (`--format:pdf` lists the formats it accepts).
- **Text that starts with a dash** goes after `--`: `puppetty type t1 -- "-x"`.

## Commands

| Command | Does |
|---|---|
| `start [--cols:N] [--rows:N] [--cwd:DIR] [NAME=VALUE ...] <program> [args ...]` | Launch; prints the session id. Defaults are 80x24 in the current directory. Leading `NAME=VALUE` words set environment variables, as in a POSIX shell. Everything from the program on is passed to it unchanged, switches included. Waits for the first screen before returning. |
| `type <id> <text>` | Type literal text; `\n` in the text is Enter. |
| `press <id> <keys ...>` | Press chords in order: `Enter`, `Escape`, `Up`, `F5`, `Ctrl+C`, `Alt+F`, `a`, ... |
| `paste <id> <text>` | Paste (bracketed when the program enabled it). |
| `click <id> <row> <col> [--button:left\|middle\|right]` | Click a cell (zero-based). Says so if the program has no mouse reporting. |
| `resize <id> <cols> <rows>` | Resize; the program gets SIGWINCH. |
| `wait <id> --text:T \| --regex:R \| --idle \| --exit [--timeout:10s]` | Wait. `--exit` prints the exit code. Durations: `500ms`, `5s`, `2m`. |
| `snap <id> [--format:text\|annotated\|ansi]` | Print the screen. |
| `scrollback <id> [--lines:N]` | Print lines that scrolled off the top. |
| `ls` | List sessions (tab-separated: id, size, state, command). |
| `close <id>` | End the program and close the session. |
| `attach <id>` | Mirror the session in this terminal (alternate screen) until it exits or Ctrl+C. Read-only. |
| `stop` | Close every session and stop the daemon. |
| `mcp` | Run the MCP server on stdin/stdout ([mcp.md](mcp.md)). Its sessions are its own, not the daemon's. |

Every action waits for the screen to settle before returning (see [api.md](api.md#settling)).
Errors go to stderr with exit code 1; a timed-out `wait` prints the screen with the error.

## The daemon

- **Starting:** the first command that needs it starts the daemon (`puppetty daemon`) with CShell's
  `Background()`. The daemon gets no window and none of the caller's handles or streams, so
  `$(puppetty start ...)` returns as soon as the id is printed. It runs in its own process group
  (Windows) or session (Linux), so Ctrl+C or closing the terminal that started it doesn't stop it.
- **Transport:** it listens on a per-user named pipe on Windows and a Unix domain socket elsewhere,
  both restricted to the current user. `PUPPETTY_PIPE` overrides the name, which gives a private
  daemon (the tests use this).
- **Stopping:** it exits after 10 minutes with no sessions and no requests
  (`PUPPETTY_DAEMON_IDLE_SECONDS` overrides this), or on `puppetty stop`. Either way it kills the
  programs it was running.
- **One per name:** only one daemon runs per pipe name. A second one started by a race exits
  straight away.
