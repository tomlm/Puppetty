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

    private Task Check(string expectation, Func<Screen, (bool, string)> check) =>
        Retry.UntilAsync(_terminal, "screen", expectation, check, _negated, _timeout);
}
