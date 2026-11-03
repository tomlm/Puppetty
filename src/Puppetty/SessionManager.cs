using System.Collections.Concurrent;

namespace Puppetty;

/// <summary>A named, running terminal held by a <see cref="SessionManager"/>.</summary>
/// <param name="Id">Short id such as <c>t1</c>.</param>
/// <param name="Command">The command line it was launched with, for display.</param>
/// <param name="Terminal">The running terminal.</param>
/// <param name="StartedAt">When it was launched.</param>
public sealed record Session(string Id, string Command, Terminal Terminal, DateTimeOffset StartedAt);

/// <summary>
/// Keeps terminals alive between calls and addresses them by short id. Used by the MCP server and
/// the CLI daemon, where each request names the session it acts on.
/// </summary>
public sealed class SessionManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private int _next;

    /// <summary>Launches a terminal and registers it under a new id.</summary>
    public async Task<Session> LaunchAsync(string command, LaunchOptions options, CancellationToken cancellationToken = default)
    {
        var terminal = await Terminal.LaunchAsync(command, options, cancellationToken).ConfigureAwait(false);
        var id = $"t{Interlocked.Increment(ref _next)}";
        var display = options.Args.Count == 0 ? command : $"{command} {string.Join(' ', options.Args)}";
        var session = new Session(id, display, terminal, DateTimeOffset.UtcNow);
        _sessions[id] = session;
        return session;
    }

    /// <summary>The session with <paramref name="id"/>.</summary>
    /// <exception cref="KeyNotFoundException">No such session; the message lists the open ones.</exception>
    public Session Get(string id)
    {
        if (_sessions.TryGetValue(id, out var session))
            return session;
        var open = _sessions.Keys.Order().ToList();
        throw new KeyNotFoundException(open.Count == 0
            ? $"No session '{id}'. No sessions are open."
            : $"No session '{id}'. Open sessions: {string.Join(", ", open)}.");
    }

    /// <summary>All open sessions, oldest first.</summary>
    public IReadOnlyList<Session> List() => _sessions.Values.OrderBy(s => s.StartedAt).ToList();

    /// <summary>Ends a session's program and forgets the session.</summary>
    /// <returns>The exit code, or null if the program had to be killed.</returns>
    public async Task<int?> CloseAsync(string id)
    {
        var session = Get(id);
        _sessions.TryRemove(id, out _);
        var code = session.Terminal.ExitCode;
        await session.Terminal.DisposeAsync().ConfigureAwait(false);
        return code;
    }

    /// <summary>Closes every session.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var id in _sessions.Keys.ToList())
        {
            if (_sessions.TryRemove(id, out var session))
                await session.Terminal.DisposeAsync().ConfigureAwait(false);
        }
    }
}
