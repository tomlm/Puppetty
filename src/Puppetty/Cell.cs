namespace Puppetty;

/// <summary>Text attributes of a cell.</summary>
[Flags]
public enum CellAttributes
{
    /// <summary>No attributes.</summary>
    None = 0,
    /// <summary>Bold (SGR 1).</summary>
    Bold = 1 << 0,
    /// <summary>Dim / faint (SGR 2).</summary>
    Dim = 1 << 1,
    /// <summary>Italic (SGR 3).</summary>
    Italic = 1 << 2,
    /// <summary>Any underline style (SGR 4, 4:x, 21).</summary>
    Underline = 1 << 3,
    /// <summary>Blink (SGR 5).</summary>
    Blink = 1 << 4,
    /// <summary>Reverse video (SGR 7). Colours are reported as set, not swapped.</summary>
    Inverse = 1 << 5,
    /// <summary>Invisible / concealed (SGR 8).</summary>
    Invisible = 1 << 6,
    /// <summary>Strikethrough (SGR 9).</summary>
    Strikethrough = 1 << 7,
    /// <summary>Overline (SGR 53).</summary>
    Overline = 1 << 8,
}

/// <summary>One cell of the screen grid.</summary>
/// <param name="Text">The grapheme in the cell; a space when empty, and "" for the right half of a wide character.</param>
/// <param name="Width">Columns the character occupies: 1, 2 for wide characters, or 0 for the right half of one.</param>
/// <param name="Foreground">Foreground colour as set by the application.</param>
/// <param name="Background">Background colour as set by the application.</param>
/// <param name="Attributes">Bold, underline, reverse video and so on.</param>
public readonly record struct Cell(string Text, int Width, Color Foreground, Color Background, CellAttributes Attributes)
{
    /// <summary>Whether the cell has all of <paramref name="attributes"/>.</summary>
    public bool Has(CellAttributes attributes) => (Attributes & attributes) == attributes;
}
