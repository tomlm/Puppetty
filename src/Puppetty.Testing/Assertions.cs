namespace Puppetty.Testing;

/// <summary>
/// Entry point for auto-retrying assertions. Import with <c>using static Puppetty.Testing.Assertions;</c>.
/// </summary>
/// <remarks>
/// Each assertion re-checks the screen every time it changes until it passes or the timeout runs
/// out, then throws <see cref="ExpectationException"/>. Every test framework reports that as a failure.
/// </remarks>
/// <example>
/// <code>
/// await Expect(term.GetByText("Ready")).ToBeVisibleAsync();
/// await Expect(term.Line(0)).ToContainTextAsync("hello");
/// await Expect(term.Cell(0, 0)).ToHaveForegroundAsync(AnsiColor.Red);
/// await Expect(term.GetByText("Loading")).Not.ToBeVisibleAsync();
/// </code>
/// </example>
public static class Assertions
{
    /// <summary>How long assertions retry when no timeout is given. Default 5 seconds.</summary>
    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Where failure artifacts (traces, actual snapshots) are written. Defaults to
    /// <c>PUPPETTY_ARTIFACTS</c>, else <c>puppetty-artifacts</c> under the current directory
    /// (the test output directory under <c>dotnet test</c>).
    /// </summary>
    public static string ArtifactsDirectory { get; set; } =
        Environment.GetEnvironmentVariable("PUPPETTY_ARTIFACTS") is { Length: > 0 } dir ? dir : Path.Combine(Environment.CurrentDirectory, "puppetty-artifacts");

    /// <summary>
    /// Where golden snapshots live. Null (the default) means a <c>__snapshots__</c> folder next to
    /// the test source file that calls <see cref="TerminalAssertions.ToMatchSnapshotAsync"/>.
    /// </summary>
    public static string? SnapshotDirectory { get; set; }

    /// <summary>Assertions about a located piece of the screen.</summary>
    public static LocatorAssertions Expect(Locator locator, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        return new LocatorAssertions(locator, timeout ?? DefaultTimeout, negated: false);
    }

    /// <summary>Assertions about one cell's text, colours and attributes.</summary>
    public static CellAssertions Expect(CellLocator cell, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return new CellAssertions(cell, timeout ?? DefaultTimeout, negated: false);
    }

    /// <summary>Assertions about the whole screen.</summary>
    public static TerminalAssertions Expect(Terminal terminal, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        return new TerminalAssertions(terminal, timeout ?? DefaultTimeout, negated: false);
    }
}

/// <summary>An assertion did not hold before its timeout.</summary>
public sealed class ExpectationException : Exception
{
    internal ExpectationException(string message, Screen screen)
        : base($"{message}\n--- screen ---\n{screen.GetText()}\n--------------")
    {
        Screen = screen;
    }

    /// <summary>The screen when the assertion gave up.</summary>
    public Screen Screen { get; }
}

/// <summary>Shared retry loop for all assertion types.</summary>
internal static class Retry
{
    /// <param name="terminal">Terminal to watch.</param>
    /// <param name="subject">What is being asserted on, e.g. <c>text "OK"</c>.</param>
    /// <param name="expectation">The positive phrasing, e.g. <c>to be visible</c>.</param>
    /// <param name="check">Returns whether the expectation holds on a screen, and what was actually there.</param>
    /// <param name="negated">Whether the assertion is <c>.Not</c>.</param>
    /// <param name="timeout">How long to retry.</param>
    public static async Task UntilAsync(Terminal terminal, string subject, string expectation, Func<Screen, (bool Holds, string Actual)> check, bool negated, TimeSpan timeout)
    {
        try
        {
            await terminal.WaitForScreenAsync(s => check(s).Holds != negated, $"{subject} {expectation}", timeout).ConfigureAwait(false);
        }
        catch (TerminalTimeoutException ex)
        {
            var actual = check(ex.Screen).Actual;
            var not = negated ? "not " : "";
            var reason = ex.OutputEnded ? " (the program's output ended)" : $" within {timeout}";
            throw new ExpectationException($"Expected {subject} {not}{expectation}{reason}.\nActual: {actual}{Artifacts.SaveTrace(terminal, subject)}", ex.Screen);
        }
    }
}

/// <summary>Writes failure artifacts into <see cref="Assertions.ArtifactsDirectory"/>.</summary>
internal static class Artifacts
{
    /// <summary>Saves the terminal's trace; returns a message line naming the file, or "" if there is none.</summary>
    public static string SaveTrace(Terminal terminal, string label)
    {
        if (!terminal.IsTracing)
            return "";
        try
        {
            var path = PathFor(label, Trace.Extension);
            terminal.GetTrace().Save(path);
            return $"\nTrace: {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"\n(could not save trace: {ex.Message})";
        }
    }

    public static string PathFor(string label, string extension)
    {
        var safe = new string(label.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        if (safe.Length > 60)
            safe = safe[..60];
        return Path.Combine(Assertions.ArtifactsDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{safe}{extension}");
    }
}
