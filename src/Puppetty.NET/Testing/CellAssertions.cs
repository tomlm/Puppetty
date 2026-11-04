namespace Puppetty.Testing;

/// <summary>Auto-retrying assertions about one cell. Create with <see cref="Assertions.Expect(CellLocator, TimeSpan?)"/>.</summary>
/// <remarks>Colours are compared as the application set them; reverse video does not swap them.</remarks>
public sealed class CellAssertions
{
    private readonly CellLocator _cell;
    private readonly TimeSpan _timeout;
    private readonly bool _negated;

    internal CellAssertions(CellLocator cell, TimeSpan timeout, bool negated)
    {
        _cell = cell;
        _timeout = timeout;
        _negated = negated;
    }

    /// <summary>The same assertions, negated.</summary>
    public CellAssertions Not => new(_cell, _timeout, !_negated);

    /// <summary>The cell's foreground colour is <paramref name="color"/>.</summary>
    public Task ToHaveForegroundAsync(Color color) =>
        Check($"to have foreground {color}", c => (c.Foreground == color, c.Foreground.ToString()));

    /// <summary>The cell's background colour is <paramref name="color"/>.</summary>
    public Task ToHaveBackgroundAsync(Color color) =>
        Check($"to have background {color}", c => (c.Background == color, c.Background.ToString()));

    /// <summary>The cell has all of <paramref name="attributes"/> (it may have others too).</summary>
    public Task ToHaveAttributesAsync(CellAttributes attributes) =>
        Check($"to have attributes {attributes}", c => (c.Has(attributes), c.Attributes.ToString()));

    /// <summary>The cell's text is <paramref name="text"/>.</summary>
    public Task ToHaveTextAsync(string text) =>
        Check($"to have text \"{text}\"", c => (c.Text == text, $"\"{c.Text}\""));

    private Task Check(string expectation, Func<Cell, (bool, string)> check) =>
        Retry.UntilAsync(_cell.Terminal, _cell.ToString(), expectation,
            s => _cell.Resolve(s) is { } cell ? check(cell) : (false, "off-screen"),
            _negated, _timeout);
}
