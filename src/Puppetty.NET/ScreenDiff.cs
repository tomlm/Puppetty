using System.Text;

namespace Puppetty;

/// <summary>What to compare in <see cref="ScreenDiff.Compare"/>.</summary>
public sealed record ScreenDiffOptions
{
    /// <summary>Compare colours and attributes, not just text. Default true.</summary>
    public bool CompareStyles { get; init; } = true;

    /// <summary>Compare the cursor position. Default false (it often differs harmlessly).</summary>
    public bool CompareCursor { get; init; }
}

/// <summary>A cell that differs between two screens.</summary>
/// <param name="Row">Zero-based row.</param>
/// <param name="Col">Zero-based column.</param>
/// <param name="Expected">The expected cell.</param>
/// <param name="Actual">The actual cell.</param>
public readonly record struct CellDifference(int Row, int Col, Cell Expected, Cell Actual)
{
    /// <summary>True when only colours or attributes differ, not the text.</summary>
    public bool StyleOnly => Expected.Text == Actual.Text && Expected.Width == Actual.Width;
}

/// <summary>The differences between an expected and an actual <see cref="Screen"/>.</summary>
public sealed class ScreenDiff
{
    private readonly Screen _expected;
    private readonly Screen _actual;

    private ScreenDiff(Screen expected, Screen actual, IReadOnlyList<CellDifference> cells, string? sizeMismatch, string? cursorMismatch)
    {
        _expected = expected;
        _actual = actual;
        Cells = cells;
        SizeMismatch = sizeMismatch;
        CursorMismatch = cursorMismatch;
    }

    /// <summary>Differing cells, row-major. Empty when the sizes differ.</summary>
    public IReadOnlyList<CellDifference> Cells { get; }

    /// <summary>Describes a size difference, or null.</summary>
    public string? SizeMismatch { get; }

    /// <summary>Describes a cursor difference (when compared), or null.</summary>
    public string? CursorMismatch { get; }

    /// <summary>True when nothing compared differs.</summary>
    public bool IsMatch => Cells.Count == 0 && SizeMismatch is null && CursorMismatch is null;

    /// <summary>Compares two screens.</summary>
    public static ScreenDiff Compare(Screen expected, Screen actual, ScreenDiffOptions? options = null)
    {
        options ??= new ScreenDiffOptions();
        if (expected.Rows != actual.Rows || expected.Cols != actual.Cols)
            return new ScreenDiff(expected, actual, [], $"size differs: expected {expected.Cols}x{expected.Rows}, actual {actual.Cols}x{actual.Rows}", null);

        var cells = new List<CellDifference>();
        for (var r = 0; r < expected.Rows; r++)
        {
            for (var c = 0; c < expected.Cols; c++)
            {
                Cell e = expected[r, c], a = actual[r, c];
                var differs = options.CompareStyles ? e != a : e.Text != a.Text;
                if (differs)
                    cells.Add(new CellDifference(r, c, e, a));
            }
        }
        var cursor = options.CompareCursor && expected.Cursor != actual.Cursor
            ? $"cursor differs: expected {expected.Cursor.Row},{expected.Cursor.Col}, actual {actual.Cursor.Row},{actual.Cursor.Col}"
            : null;
        return new ScreenDiff(expected, actual, cells, null, cursor);
    }

    /// <summary>
    /// Side-by-side text: expected on the left, actual on the right, row numbers in the middle. Under
    /// each differing row a marker line shows <c>^</c> where text differs and <c>~</c> where only
    /// the style does. Matching rows are elided, keeping one row of context around each change.
    /// </summary>
    public override string ToString()
    {
        if (IsMatch)
            return "screens match";
        var sb = new StringBuilder();
        if (SizeMismatch is not null)
        {
            sb.Append(SizeMismatch).Append('\n');
            sb.Append("--- expected ---\n").Append(_expected.GetText()).Append("\n--- actual ---\n").Append(_actual.GetText()).Append('\n');
            return sb.ToString();
        }
        if (CursorMismatch is not null)
            sb.Append(CursorMismatch).Append('\n');
        if (Cells.Count > 0)
        {
            var styleOnly = Cells.Count(d => d.StyleOnly);
            sb.Append($"{Cells.Count} cell(s) differ").Append(styleOnly > 0 ? $" ({styleOnly} in style only)" : "").Append('\n');
        }

        var width = _expected.Cols;
        var numberWidth = (_expected.Rows - 1).ToString().Length;
        var gutter = new string(' ', numberWidth);
        sb.Append("expected".PadRight(width)).Append(' ').Append(gutter).Append(" │ actual\n");

        var changedRows = Cells.Select(d => d.Row).ToHashSet();
        var lastShown = -1;
        for (var r = 0; r < _expected.Rows; r++)
        {
            var near = changedRows.Contains(r) || changedRows.Contains(r - 1) || changedRows.Contains(r + 1);
            if (!near)
                continue;
            if (lastShown >= 0 && r > lastShown + 1)
                sb.Append("...\n");
            lastShown = r;

            sb.Append(FullRow(_expected, r)).Append(' ').Append(r.ToString().PadLeft(numberWidth)).Append(" │ ").Append(_actual.Line(r)).Append('\n');
            if (changedRows.Contains(r))
            {
                var marks = new char[width];
                Array.Fill(marks, ' ');
                foreach (var d in Cells.Where(d => d.Row == r))
                    marks[d.Col] = d.StyleOnly ? '~' : '^';
                var markLine = new string(marks).TrimEnd();
                sb.Append(new string(' ', width)).Append(' ').Append(gutter).Append(" │ ").Append(markLine).Append('\n');
            }
        }
        return sb.ToString();
    }

    // The full-width row: every cell's text, so it spans exactly Cols display columns (a wide
    // character's right half is ""), keeping the right-hand column aligned.
    private static string FullRow(Screen s, int row)
    {
        var sb = new StringBuilder();
        for (var c = 0; c < s.Cols; c++)
            sb.Append(s[row, c].Text);
        return sb.ToString();
    }
}
