using System.Text;

namespace Puppetty;

/// <summary>How to render a <see cref="Screen"/> as text.</summary>
public enum ScreenFormat
{
    /// <summary>Plain text: rows joined with <c>\n</c>, trailing spaces trimmed.</summary>
    Text,

    /// <summary>
    /// For AI agents: a one-line header (size, cursor, active modes, title), a short list of styled
    /// spans (reverse video usually marks a selection), then the grid with row numbers. Trailing
    /// blank rows are collapsed.
    /// </summary>
    Annotated,

    /// <summary>The grid with SGR escape sequences for colours and attributes, for display in a real terminal.</summary>
    Ansi,
}

internal static class ScreenFormatter
{
    private const int MaxSpans = 30;
    private const string Esc = "\u001b";

    public static string Annotated(Screen s)
    {
        var sb = new StringBuilder();

        var header = new List<string> { $"size {s.Cols}x{s.Rows}", $"cursor {s.Cursor.Row},{s.Cursor.Col}{(s.CursorVisible ? "" : " hidden")}" };
        if (s.IsAlternateScreen) header.Add("alt-screen");
        var m = s.Modes;
        if (m.Mouse != MouseTracking.None) header.Add($"mouse {m.Mouse.ToString().ToLowerInvariant()}/{m.MouseEncoding}");
        if (m.BracketedPaste) header.Add("bracketed-paste");
        if (m.ApplicationCursorKeys) header.Add("app-cursor-keys");
        if (m.KittyKeyboardFlags != 0) header.Add($"kitty-keys {m.KittyKeyboardFlags}");
        if (m.SynchronizedOutput) header.Add("mid-frame");
        if (s.Title.Length > 0) header.Add($"title \"{s.Title}\"");
        sb.Append(string.Join(" · ", header)).Append('\n');

        var spans = StyledSpans(s);
        if (spans.Count > 0)
        {
            sb.Append("styles: ").Append(string.Join("; ", spans.Take(MaxSpans)));
            if (spans.Count > MaxSpans) sb.Append($"; +{spans.Count - MaxSpans} more");
            sb.Append('\n');
        }

        var lastRow = s.Rows - 1;
        while (lastRow > s.Cursor.Row && s.Line(lastRow).Length == 0)
            lastRow--;
        var width = (s.Rows - 1).ToString().Length;
        for (var r = 0; r <= lastRow; r++)
            sb.Append(r.ToString().PadLeft(width)).Append('│').Append(s.Line(r)).Append('\n');
        if (lastRow < s.Rows - 1)
            sb.Append($"(rows {lastRow + 1}-{s.Rows - 1} blank)\n");
        return sb.ToString();
    }

    /// <summary>Runs of cells on one row sharing the same non-default style, as "r1 5-7 inverse".</summary>
    private static List<string> StyledSpans(Screen s)
    {
        var spans = new List<string>();
        for (var r = 0; r < s.Rows; r++)
        {
            var c = 0;
            while (c < s.Cols)
            {
                var style = Describe(s[r, c]);
                if (style is null)
                {
                    c++;
                    continue;
                }
                var start = c;
                while (c + 1 < s.Cols && Describe(s[r, c + 1]) == style)
                    c++;
                // Styled runs of spaces are kept: a filled bar shows where a selection or status line is.
                spans.Add(start == c ? $"r{r} {start} {style}" : $"r{r} {start}-{c} {style}");
                c++;
            }
        }
        return spans;
    }

    private static string? Describe(Cell cell)
    {
        if (cell.Foreground.Kind == ColorKind.Default && cell.Background.Kind == ColorKind.Default && cell.Attributes == CellAttributes.None)
            return null;
        var parts = new List<string>();
        if (cell.Foreground.Kind != ColorKind.Default) parts.Add("fg=" + Short(cell.Foreground));
        if (cell.Background.Kind != ColorKind.Default) parts.Add("bg=" + Short(cell.Background));
        foreach (var flag in Enum.GetValues<CellAttributes>())
        {
            if (flag != CellAttributes.None && cell.Has(flag))
                parts.Add(flag.ToString().ToLowerInvariant());
        }
        return string.Join(' ', parts);
    }

    private static string Short(Color c) => c.Kind switch
    {
        ColorKind.Palette when c.Value < 16 => ((AnsiColor)c.Value).ToString().ToLowerInvariant(),
        ColorKind.Palette => $"p{c.Value}",
        _ => $"#{c.Value:x6}",
    };

    public static string Ansi(Screen s)
    {
        var sb = new StringBuilder();
        for (var r = 0; r < s.Rows; r++)
        {
            if (r > 0) sb.Append("\r\n");

            // Stop after the last cell that shows anything: text, or a style such as a filled bar.
            var last = s.Cols - 1;
            while (last >= 0 && s[r, last].Text == " " && Sgr(s[r, last]).Length == 0)
                last--;

            var current = "";
            for (var c = 0; c <= last; c++)
            {
                var cell = s[r, c];
                if (cell.Width == 0) continue;
                var sgr = Sgr(cell);
                if (sgr != current)
                {
                    sb.Append(Esc).Append("[0").Append(sgr).Append('m');
                    current = sgr;
                }
                sb.Append(cell.Text);
            }
            if (current.Length > 0) sb.Append(Esc).Append("[0m");
        }
        return sb.ToString();
    }

    /// <summary>The SGR parameters (each prefixed with ';') for a cell's style; "" for the default style.</summary>
    private static string Sgr(Cell cell)
    {
        var sb = new StringBuilder();
        var a = cell.Attributes;
        if ((a & CellAttributes.Bold) != 0) sb.Append(";1");
        if ((a & CellAttributes.Dim) != 0) sb.Append(";2");
        if ((a & CellAttributes.Italic) != 0) sb.Append(";3");
        if ((a & CellAttributes.Underline) != 0) sb.Append(";4");
        if ((a & CellAttributes.Blink) != 0) sb.Append(";5");
        if ((a & CellAttributes.Inverse) != 0) sb.Append(";7");
        if ((a & CellAttributes.Invisible) != 0) sb.Append(";8");
        if ((a & CellAttributes.Strikethrough) != 0) sb.Append(";9");
        if ((a & CellAttributes.Overline) != 0) sb.Append(";53");
        AppendColor(sb, cell.Foreground, 30, 90, 38);
        AppendColor(sb, cell.Background, 40, 100, 48);
        return sb.ToString();
    }

    private static void AppendColor(StringBuilder sb, Color c, int basic, int bright, int extended)
    {
        switch (c.Kind)
        {
            case ColorKind.Palette when c.Value < 8: sb.Append(';').Append(basic + c.Value); break;
            case ColorKind.Palette when c.Value < 16: sb.Append(';').Append(bright + c.Value - 8); break;
            case ColorKind.Palette: sb.Append(';').Append(extended).Append(";5;").Append(c.Value); break;
            case ColorKind.Rgb: sb.Append(';').Append(extended).Append(";2;").Append((c.Value >> 16) & 0xFF).Append(';').Append((c.Value >> 8) & 0xFF).Append(';').Append(c.Value & 0xFF); break;
        }
    }
}
