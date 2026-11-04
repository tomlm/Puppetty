# `puppetty` CLI

Drive terminal programs from a shell or script. Sessions live in a background daemon, so each
command is a separate, short invocation.

```sh
dotnet tool install -g Puppetty.Cli        # or: dotnet run --project src/Puppetty.Cli -- <command>

id=$(puppetty start --cmd "python3 -i -q" --cols 80 --rows 24)   # prints the session id, e.g. t1
puppetty type    --id $id "print(6*7)"
puppetty press   --id $id Enter
puppetty wait    --id $id --text ">>> " --timeout 5s
puppetty snap    --id $id                  # --format text|annotated|ansi
puppetty ls
puppetty attach  --id $id                  # live read-only view; Ctrl+C detaches
puppetty close   --id $id                  # prints the exit code, or "killed"
```

## Commands

| Command | Does |
|---|---|
| `start --cmd "<command line>"` or `start -- <program> <args...>` | Launch; prints the session id. Options `--cols` (80), `--rows` (24), `--cwd`, `--env KEY=VALUE` (repeatable). Waits for the first screen before returning. |
| `type --id <id> <text>` | Type literal text; `\n` in the text is Enter. |
| `press --id <id> <keys...>` | Press chords in order: `Enter`, `Escape`, `Up`, `F5`, `Ctrl+C`, `Alt+F`, `a`, ... |
| `paste --id <id> <text>` | Paste (bracketed when the program enabled it). |
| `click --id <id> --row R --col C [--button left\|middle\|right]` | Click a cell (zero-based). Says so if the program has no mouse reporting. |
| `resize --id <id> --cols C --rows R` | Resize; the program gets SIGWINCH. |
| `wait --id <id> --text T \| --regex R \| --idle \| --exit [--timeout 10s]` | Wait. `--exit` prints the exit code. Durations: `500ms`, `5s`, `2m`. |
| `snap --id <id> [--format text\|annotated\|ansi]` | Print the screen. |
| `scrollback --id <id> [--lines N]` | Print lines that scrolled off the top. |
| `ls` | List sessions (tab-separated: id, size, state, command). |
| `close --id <id>` | End the program and close the session. |
| `attach --id <id>` | Mirror the session in this terminal (alternate screen) until it exits or Ctrl+C. Read-only. |
| `stop` | Close every session and stop the daemon. |

Every action waits for the screen to settle before returning (see [api.md](api.md#settling)).
Errors go to stderr with exit code 1; a timed-out `wait` prints the screen with the error.

`--cmd` is split on whitespace, with single or double quotes grouping words. Backslashes are
literal, so Windows paths work unquoted unless they contain spaces.

## The daemon

- **Starting:** the first command that needs it starts the daemon (`puppetty daemon`, hidden). It's
  detached from the calling terminal and inherits none of its handles, so `$(puppetty start ...)`
  returns as soon as the id is printed.
- **Transport:** it listens on a per-user named pipe on Windows and a Unix domain socket elsewhere,
  both restricted to the current user. `PUPPETTY_PIPE` overrides the name, which gives a private
  daemon (the tests use this).
- **Stopping:** it exits after 10 minutes with no sessions and no requests
  (`PUPPETTY_DAEMON_IDLE_SECONDS` overrides this), or on `puppetty stop`. Either way it kills the
  programs it was running.
- **One per name:** only one daemon runs per pipe name. A second one started by a race exits
  straight away.
