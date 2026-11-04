using System.Text.RegularExpressions;

namespace Puppetty.Testing;

/// <summary>Auto-retrying assertions about the whole screen. Create with <see cref="Assertions.Expect(Terminal, TimeSpan?)"/>.</summary>
public sealed class TerminalAssertions
{
    private readonly Terminal _terminal;
    private readonly TimeSpan _timeout;
    private readonly bool _negated;

    internal TerminalAssertions(Terminal terminal, TimeSpan timeout, bool negated)
    {
        _terminal = terminal;
        _timeout = timeout;
        _negated = negated;
    }

    /// <summary>The same assertions, negated.</summary>
    public TerminalAssertions Not => new(_terminal, _timeout, !_negated);

    /// <summary><paramref name="text"/> appears on some row of the screen.</summary>
    public Task ToContainTextAsync(string text) =>
        Check($"to contain text \"{text}\"", s => (s.Contains(text), "see screen"));

    /// <summary>Some row of the screen matches <paramref name="pattern"/>.</summary>
    public Task ToContainTextAsync(Regex pattern) =>
        Check($"to contain text matching /{pattern}/", s => (s.Contains(pattern), "see screen"));

    /// <summary>The cursor is at (<paramref name="row"/>, <paramref name="col"/>).</summary>
    public Task ToHaveCursorAtAsync(int row, int col) =>
        Check($"to have the cursor at ({row}, {col})", s => (s.Cursor == (row, col), $"({s.Cursor.Row}, {s.Cursor.Col})"));

    /// <summary>The alternate screen (used by full-screen apps) is active.</summary>
    public Task ToBeOnAlternateScreenAsync() =>
        Check("to be on the alternate screen", s => (s.IsAlternateScreen, s.IsAlternateScreen ? "alternate screen" : "normal screen"));

    /// <summary>
    /// The screen matches the golden snapshot <paramref name="name"/>, retrying until it does or the
    /// timeout runs out.
    /// </summary>
    /// <remarks>
    /// <para>Golden files are <c>__snapshots__/&lt;name&gt;.puppetty-snap</c> next to the calling test file
    /// (or in <see cref="Assertions.SnapshotDirectory"/>).</para>
    /// <para>A missing golden is created from the screen once it settles, and the assertion passes;
    /// under CI (the <c>CI</c> environment variable is set) a missing golden fails instead. Set
    /// <c>PUPPETTY_UPDATE_SNAPSHOTS=1</c> to rewrite goldens from the current screens.</para>
    /// <para>On failure the message holds a side-by-side diff, and the actual screen and the trace
    /// are written to <see cref="Assertions.ArtifactsDirectory"/>.</para>
    /// </remarks>
    public async Task ToMatchSnapshotAsync(string name, ScreenDiffOptions? options = null, [System.Runtime.CompilerServices.CallerFilePath] string callerFile = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var directory = Assertions.SnapshotDirectory ?? Path.Combine(Path.GetDirectoryName(callerFile) ?? Environment.CurrentDirectory, "__snapshots__");
        var golden = Path.Combine(directory, name + Snapshot.Extension);

        var update = Environment.GetEnvironmentVariable("PUPPETTY_UPDATE_SNAPSHOTS") is "1" or "true";
        if (update || !File.Exists(golden))
        {
            if (!update && Environment.GetEnvironmentVariable("CI") is { Length: > 0 })
                throw new ExpectationException($"Snapshot '{name}' does not exist at {golden}. Run the test locally to create it, and commit it.", _terminal.Screen);
            try { await _terminal.WaitForIdleAsync(_timeout); }
            catch (TerminalTimeoutException) { }
            Snapshot.Save(_terminal.Screen, golden);
            return;
        }

        var expected = Snapshot.Load(golden);
        try
        {
            await _terminal.WaitForScreenAsync(s => ScreenDiff.Compare(expected, s, options).IsMatch != _negated, $"snapshot '{name}'", _timeout);
        }
        catch (TerminalTimeoutException ex)
        {
            if (_negated)
                throw new ExpectationException($"Expected the screen not to match snapshot '{name}', but it did.", ex.Screen);
            var actualPath = Artifacts.PathFor(name, ".actual" + Snapshot.Extension);
            Snapshot.Save(ex.Screen, actualPath);
            var diff = ScreenDiff.Compare(expected, ex.Screen, options);
            throw new ExpectationException(
                $"Screen does not match snapshot '{name}' ({golden}).\n{diff}\nActual snapshot: {actualPath}{Artifacts.SaveTrace(_terminal, name)}\n" +
                "Set PUPPETTY_UPDATE_SNAPSHOTS=1 to accept the new screen.",
                ex.Screen);
        }
    }

    private Task Check(string expectation, Func<Screen, (bool, string)> check) =>
        Retry.UntilAsync(_terminal, "screen", expectation, check, _negated, _timeout);
}
