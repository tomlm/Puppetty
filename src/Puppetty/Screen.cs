using System.Text;
using XTerm.Buffer;
using XTerm.Common;
using Emulator = XTerm.Terminal;

namespace Puppetty;

/// <summary>An immutable snapshot of the visible terminal screen.</summary>
public sealed class Screen
{
    private readonly Cell[,] _cells;
    private readonly Row[] _rows;

    private Screen(Cell[,] cells, Row[] rows, int cursorRow, int cursorCol, bool cursorVisible, bool alternateScreen, TerminalModes modes, string title)
    {
        _cells = cells;
        _rows = rows;
        Cursor = (cursorRow, cursorCol);
        CursorVisible = cursorVisible;
        IsAlternateScreen = alternateScreen;
        Modes = modes;
        Title = title;
    }

    /// <summary>Width in columns.</summary>
    public int Cols => _cells.GetLength(1);

    /// <summary>Height in rows.</summary>
    public int Rows => _cells.GetLength(0);

    /// <summary>Cursor position, zero-based.</summary>
    public (int Row, int Col) Cursor { get; }

    /// <summary>Whether the application has the cursor shown (DECTCEM).</summary>
    public bool CursorVisible { get; }

    /// <summary>Whether the alternate screen (full-screen apps) is active.</summary>
    public bool IsAlternateScreen { get; }

    /// <summary>Input-relevant modes the application had set (mouse, bracketed paste, cursor keys, ...).</summary>
    public TerminalModes Modes { get; }

    /// <summary>The window title the application set (OSC 0/2), or "".</summary>
    public string Title { get; }

    /// <summary>Renders the screen in <paramref name="format"/>. See <see cref="ScreenFormat"/>.</summary>
    public string Render(ScreenFormat format) => format switch
    {
        ScreenFormat.Text => GetText(),
        ScreenFormat.Annotated => ScreenFormatter.Annotated(this),
        ScreenFormat.Ansi => ScreenFormatter.Ansi(this),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>The cell at a zero-based position.</summary>
    public Cell this[int row, int col] => _cells[row, col];

    /// <summary>All rows, with trailing spaces trimmed.</summary>
    public IReadOnlyList<string> Lines => _rows.Select(r => r.Trimmed).ToArray();

    /// <summary>One row's text with trailing spaces trimmed.</summary>
    /// <param name="row">Zero-based row.</param>
    public string Line(int row) => _rows[row].Trimmed;

    /// <summary>The whole screen as text: rows joined with <c>\n</c>, trailing spaces trimmed per row.</summary>
    public string GetText() => string.Join('\n', _rows.Select(r => r.Trimmed));

    /// <summary>
    /// The text of a rectangle: its rows joined with <c>\n</c>, trailing spaces trimmed per row.
    /// The rectangle is clipped to the screen.
    /// </summary>
    public string GetText(int row, int col, int width, int height)
    {
        // long: callers pass int.MaxValue for "to the edge".
        var endRow = (int)Math.Min((long)row + height, Rows);
        var endCol = (int)Math.Min((long)col + width, Cols);
        var lines = new List<string>();
        for (var r = Math.Max(row, 0); r < endRow; r++)
        {
            var sb = new StringBuilder();
            for (var c = Math.Max(col, 0); c < endCol; c++)
                sb.Append(_cells[r, c].Text);
            lines.Add(sb.ToString().TrimEnd(' '));
        }
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Whether <paramref name="text"/> appears on any single row. Rows are matched at full width, so
    /// trailing spaces count: a prompt such as <c>"&gt;&gt;&gt; "</c> matches.
    /// </summary>
    public bool Contains(string text) => _rows.Any(r => r.Padded.Contains(text, StringComparison.Ordinal));

    /// <summary>Whether <paramref name="pattern"/> matches any single row (at full width).</summary>
    public bool Contains(System.Text.RegularExpressions.Regex pattern) => _rows.Any(r => pattern.IsMatch(r.Padded));

    /// <inheritdoc />
    public override string ToString() => GetText();

    /// <summary>A row at full width (trailing spaces kept), for text search.</summary>
    internal string PaddedLine(int row) => _rows[row].Padded;

    /// <summary>The column of the character at <paramref name="index"/> in <see cref="PaddedLine"/>.</summary>
    internal int ColumnOf(int row, int index)
    {
        var columns = _rows[row].Columns;
        return index < columns.Length ? columns[index] : Cols;
    }

    /// <summary>Captures the active screen. The caller holds the emulator's lock.</summary>
    internal static Screen Capture(Emulator emulator)
    {
        var buffer = emulator.Buffer;
        int rows = emulator.Rows, cols = emulator.Cols;
        var cells = new Cell[rows, cols];
        var rowText = new Row[rows];
        var sb = new StringBuilder(cols);
        var columns = new List<int>(cols);

        for (var r = 0; r < rows; r++)
        {
            // The active screen starts at YBase; rows before it are scrollback.
            var line = buffer.GetLine(buffer.YBase + r);
            sb.Clear();
            columns.Clear();
            for (var c = 0; c < cols; c++)
            {
                var cell = line is not null && c < line.Length ? ToCell(line[c]) : Blank;
                cells[r, c] = cell;
                // A row's string has one entry per character; remember which column each came from
                // so a match in the string maps back to a screen position past wide characters.
                foreach (var ch in cell.Text)
                {
                    sb.Append(ch);
                    columns.Add(c);
                }
            }
            var padded = sb.ToString();
            rowText[r] = new Row(padded, padded.TrimEnd(' '), columns.ToArray());
        }
        return new Screen(cells, rowText, buffer.Y, buffer.X, emulator.CursorVisible, emulator.IsAlternateBufferActive,
            TerminalModes.Capture(emulator), emulator.Title ?? "");
    }

    private static readonly Cell Blank = new(" ", 1, Color.Default, Color.Default, CellAttributes.None);

    private static Cell ToCell(BufferCell cell)
    {
        var a = cell.Attributes;
        var text = cell.Width == 0 ? "" : cell.Content;
        if (text.Length == 0 && cell.Width != 0)
            text = " ";
        return new Cell(text, cell.Width, ToColor(a.GetFgColorKind(), a.GetFgColor()), ToColor(a.GetBgColorKind(), a.GetBgColor()), ToAttributes(a));
    }

    private static Color ToColor(ColorMode kind, int value) => kind switch
    {
        ColorMode.Default => Color.Default,
        ColorMode.RGB => Color.Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value),
        _ => Color.Palette(value & 0xFF),
    };

    private static CellAttributes ToAttributes(AttributeData a)
    {
        var result = CellAttributes.None;
        if (a.IsBold()) result |= CellAttributes.Bold;
        if (a.IsDim()) result |= CellAttributes.Dim;
        if (a.IsItalic()) result |= CellAttributes.Italic;
        if (a.IsUnderline()) result |= CellAttributes.Underline;
        if (a.IsBlink()) result |= CellAttributes.Blink;
        if (a.IsInverse()) result |= CellAttributes.Inverse;
        if (a.IsInvisible()) result |= CellAttributes.Invisible;
        if (a.IsStrikethrough()) result |= CellAttributes.Strikethrough;
        if (a.IsOverline()) result |= CellAttributes.Overline;
        return result;
    }

    private readonly record struct Row(string Padded, string Trimmed, int[] Columns);
}
