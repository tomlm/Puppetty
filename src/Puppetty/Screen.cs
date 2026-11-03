using Emulator = XTerm.Terminal;

namespace Puppetty;

/// <summary>An immutable snapshot of the visible terminal screen.</summary>
public sealed class Screen
{
    private readonly string[] _lines;
    private readonly string[] _paddedLines;

    private Screen(string[] lines, string[] paddedLines, int cols, int cursorRow, int cursorCol, bool cursorVisible, bool alternateScreen)
    {
        _lines = lines;
        _paddedLines = paddedLines;
        Cols = cols;
        Cursor = (cursorRow, cursorCol);
        CursorVisible = cursorVisible;
        IsAlternateScreen = alternateScreen;
    }

    /// <summary>Width in columns.</summary>
    public int Cols { get; }

    /// <summary>Height in rows.</summary>
    public int Rows => _lines.Length;

    /// <summary>Cursor position, zero-based.</summary>
    public (int Row, int Col) Cursor { get; }

    /// <summary>Whether the application has the cursor shown (DECTCEM).</summary>
    public bool CursorVisible { get; }

    /// <summary>Whether the alternate screen (full-screen apps) is active.</summary>
    public bool IsAlternateScreen { get; }

    /// <summary>All rows, with trailing spaces trimmed.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>One row's text with trailing spaces trimmed.</summary>
    /// <param name="row">Zero-based row.</param>
    public string Line(int row) => _lines[row];

    /// <summary>The whole screen as text: rows joined with <c>\n</c>, trailing spaces trimmed per row.</summary>
    public string GetText() => string.Join('\n', _lines);

    /// <summary>
    /// Whether <paramref name="text"/> appears on any single row. Rows are matched at full width, so
    /// trailing spaces count: a prompt such as <c>"&gt;&gt;&gt; "</c> matches.
    /// </summary>
    public bool Contains(string text) => _paddedLines.Any(l => l.Contains(text, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => GetText();

    /// <summary>Captures the active screen. The caller holds the emulator's lock.</summary>
    internal static Screen Capture(Emulator emulator)
    {
        var buffer = emulator.Buffer;
        var lines = new string[emulator.Rows];
        var padded = new string[emulator.Rows];
        for (var row = 0; row < lines.Length; row++)
        {
            // The active screen starts at YBase; rows before it are scrollback.
            var line = buffer.GetLine(buffer.YBase + row);
            padded[row] = line?.TranslateToString(trimRight: false) ?? new string(' ', emulator.Cols);
            lines[row] = padded[row].TrimEnd(' ');
        }
        return new Screen(lines, padded, emulator.Cols, buffer.Y, buffer.X, emulator.CursorVisible, emulator.IsAlternateBufferActive);
    }
}
