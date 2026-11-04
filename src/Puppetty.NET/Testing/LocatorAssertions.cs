using System.Text.RegularExpressions;

namespace Puppetty.Testing;

/// <summary>Auto-retrying assertions about a <see cref="Locator"/>. Create with <see cref="Assertions.Expect(Locator, TimeSpan?)"/>.</summary>
public sealed class LocatorAssertions
{
    private readonly Locator _locator;
    private readonly TimeSpan _timeout;
    private readonly bool _negated;

    internal LocatorAssertions(Locator locator, TimeSpan timeout, bool negated)
    {
        _locator = locator;
        _timeout = timeout;
        _negated = negated;
    }

    /// <summary>The same assertions, negated.</summary>
    public LocatorAssertions Not => new(_locator, _timeout, !_negated);

    /// <summary>The locator matches something on the screen.</summary>
    public Task ToBeVisibleAsync() =>
        Check("to be visible", s => _locator.Resolve(s) is { Count: > 0 } m ? (true, Describe(m)) : (false, "not found"));

    /// <summary>The locator matches exactly <paramref name="count"/> times.</summary>
    public Task ToHaveCountAsync(int count) =>
        Check($"to have count {count}", s => _locator.Resolve(s) is var m && m.Count == count ? (true, Describe(m)) : (false, $"count {m.Count}"));

    /// <summary>The first match's text equals <paramref name="expected"/>.</summary>
    public Task ToHaveTextAsync(string expected) =>
        CheckText($"to have text \"{expected}\"", t => t == expected);

    /// <summary>The first match's text matches <paramref name="pattern"/>.</summary>
    public Task ToHaveTextAsync(Regex pattern) =>
        CheckText($"to have text matching /{pattern}/", pattern.IsMatch);

    /// <summary>The first match's text contains <paramref name="expected"/>.</summary>
    public Task ToContainTextAsync(string expected) =>
        CheckText($"to contain text \"{expected}\"", t => t.Contains(expected, StringComparison.Ordinal));

    private Task CheckText(string expectation, Func<string, bool> predicate) =>
        Check(expectation, s => _locator.Resolve(s) is [var first, ..]
            ? (predicate(first.Text), $"\"{first.Text}\"")
            : (false, "not found"));

    private Task Check(string expectation, Func<Screen, (bool, string)> check) =>
        Retry.UntilAsync(_locator.Terminal, _locator.Description, expectation, check, _negated, _timeout);

    private static string Describe(IReadOnlyList<ScreenMatch> matches) =>
        matches.Count == 0
            ? "not found"
            : string.Join(", ", matches.Take(5).Select(m => $"\"{m.Text}\" at ({m.Row}, {m.Col})")) + (matches.Count > 5 ? $" and {matches.Count - 5} more" : "");
}
