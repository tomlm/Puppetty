using System.Text.RegularExpressions;

namespace Puppetty;

/// <summary>A piece of text found on the screen, at a zero-based position.</summary>
/// <param name="Row">Row of the first character.</param>
/// <param name="Col">Column of the first character.</param>
/// <param name="Text">The matched text (for a region, its rows joined with <c>\n</c>).</param>
public readonly record struct ScreenMatch(int Row, int Col, string Text);

/// <summary>
/// A lazy description of something on the screen. Nothing is looked up until it is used, and every
/// use re-evaluates it against the current screen, so a locator stays valid as the screen changes.
/// </summary>
/// <remarks>Create locators with <see cref="Terminal.GetByText(string)"/>, <see cref="Terminal.Region"/> and <see cref="Terminal.Line"/>.</remarks>
public abstract class Locator
{
    private protected Locator(Terminal terminal, string description)
    {
        Terminal = terminal;
        Description = description;
    }

    /// <summary>The terminal this locator reads.</summary>
    public Terminal Terminal { get; }

    /// <summary>Human-readable description, used in failure messages.</summary>
    public string Description { get; }

    /// <summary>Evaluates the locator against <paramref name="screen"/>.</summary>
    /// <returns>Every match, top to bottom and left to right; empty when there is none.</returns>
    public abstract IReadOnlyList<ScreenMatch> Resolve(Screen screen);

    /// <summary>Whether the locator matches the current screen.</summary>
    public bool IsVisible => Resolve(Terminal.Screen).Count > 0;

    /// <summary>Number of matches on the current screen.</summary>
    public int Count => Resolve(Terminal.Screen).Count;

    /// <summary>The first match's text on the current screen, or null when there is none.</summary>
    public string? Text => Resolve(Terminal.Screen) is [var first, ..] ? first.Text : null;

    /// <summary>Waits until the locator matches, and returns the first match.</summary>
    /// <exception cref="TerminalTimeoutException">It did not match in time.</exception>
    public async Task<ScreenMatch> WaitForAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var screen = await Terminal.WaitForScreenAsync(s => Resolve(s).Count > 0, Description, timeout, cancellationToken).ConfigureAwait(false);
        return Resolve(screen)[0];
    }

    /// <inheritdoc />
    public override string ToString() => Description;
}

/// <summary>Finds text on single rows: a literal string or a regular expression.</summary>
internal sealed class TextLocator : Locator
{
    private readonly string? _text;
    private readonly Regex? _regex;

    public TextLocator(Terminal terminal, string text) : base(terminal, $"text \"{text}\"") => _text = text;

    public TextLocator(Terminal terminal, Regex regex) : base(terminal, $"text /{regex}/") => _regex = regex;

    public override IReadOnlyList<ScreenMatch> Resolve(Screen screen)
    {
        var matches = new List<ScreenMatch>();
        for (var row = 0; row < screen.Rows; row++)
        {
            var line = screen.PaddedLine(row);
            if (_regex is not null)
            {
                foreach (Match m in _regex.Matches(line))
                {
                    if (m.Length > 0)
                        matches.Add(new ScreenMatch(row, screen.ColumnOf(row, m.Index), m.Value));
                }
            }
            else if (_text!.Length > 0)
            {
                for (var i = line.IndexOf(_text, StringComparison.Ordinal); i >= 0; i = line.IndexOf(_text, i + _text.Length, StringComparison.Ordinal))
                    matches.Add(new ScreenMatch(row, screen.ColumnOf(row, i), _text));
            }
        }
        return matches;
    }
}

/// <summary>A fixed rectangle of the screen. It matches when it contains any non-space text.</summary>
internal sealed class RegionLocator : Locator
{
    private readonly int _row, _col, _width, _height;

    public RegionLocator(Terminal terminal, int row, int col, int width, int height, string? description = null)
        : base(terminal, description ?? $"region (row {row}, col {col}, {width}x{height})")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(col);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        (_row, _col, _width, _height) = (row, col, width, height);
    }

    public override IReadOnlyList<ScreenMatch> Resolve(Screen screen)
    {
        var text = screen.GetText(_row, _col, _width, _height);
        return string.IsNullOrWhiteSpace(text) ? [] : [new ScreenMatch(_row, _col, text)];
    }
}

/// <summary>A lazy reference to one cell, re-read from the current screen on each use.</summary>
public sealed class CellLocator
{
    internal CellLocator(Terminal terminal, int row, int col)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(col);
        Terminal = terminal;
        Row = row;
        Col = col;
    }

    /// <summary>The terminal this locator reads.</summary>
    public Terminal Terminal { get; }

    /// <summary>Zero-based row.</summary>
    public int Row { get; }

    /// <summary>Zero-based column.</summary>
    public int Col { get; }

    /// <summary>The cell on <paramref name="screen"/>, or null when the position is off-screen.</summary>
    public Cell? Resolve(Screen screen) => Row < screen.Rows && Col < screen.Cols ? screen[Row, Col] : null;

    /// <summary>The cell on the current screen, or null when the position is off-screen.</summary>
    public Cell? Value => Resolve(Terminal.Screen);

    /// <inheritdoc />
    public override string ToString() => $"cell (row {Row}, col {Col})";
}
