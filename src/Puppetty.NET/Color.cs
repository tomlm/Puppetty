namespace Puppetty;

/// <summary>The 16 standard ANSI colours, as palette indices 0–15.</summary>
public enum AnsiColor
{
    /// <summary>Palette 0 (SGR 30/40).</summary>
    Black = 0,
    /// <summary>Palette 1 (SGR 31/41).</summary>
    Red = 1,
    /// <summary>Palette 2 (SGR 32/42).</summary>
    Green = 2,
    /// <summary>Palette 3 (SGR 33/43).</summary>
    Yellow = 3,
    /// <summary>Palette 4 (SGR 34/44).</summary>
    Blue = 4,
    /// <summary>Palette 5 (SGR 35/45).</summary>
    Magenta = 5,
    /// <summary>Palette 6 (SGR 36/46).</summary>
    Cyan = 6,
    /// <summary>Palette 7 (SGR 37/47).</summary>
    White = 7,
    /// <summary>Palette 8 (SGR 90/100).</summary>
    BrightBlack = 8,
    /// <summary>Palette 9 (SGR 91/101).</summary>
    BrightRed = 9,
    /// <summary>Palette 10 (SGR 92/102).</summary>
    BrightGreen = 10,
    /// <summary>Palette 11 (SGR 93/103).</summary>
    BrightYellow = 11,
    /// <summary>Palette 12 (SGR 94/104).</summary>
    BrightBlue = 12,
    /// <summary>Palette 13 (SGR 95/105).</summary>
    BrightMagenta = 13,
    /// <summary>Palette 14 (SGR 96/106).</summary>
    BrightCyan = 14,
    /// <summary>Palette 15 (SGR 97/107).</summary>
    BrightWhite = 15,
}

/// <summary>How a <see cref="Color"/> is specified.</summary>
public enum ColorKind
{
    /// <summary>The terminal's default foreground or background.</summary>
    Default,
    /// <summary>An index into the 256-colour palette (0–15 are the ANSI colours).</summary>
    Palette,
    /// <summary>A direct 24-bit colour.</summary>
    Rgb,
}

/// <summary>
/// A cell colour as the application set it: the default colour, a palette index, or 24-bit RGB.
/// It is not resolved through a theme, so palette 1 stays palette 1.
/// </summary>
public readonly record struct Color
{
    private Color(ColorKind kind, int value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>How the colour is specified.</summary>
    public ColorKind Kind { get; }

    /// <summary>The palette index (0–255) or <c>0xRRGGBB</c>; 0 for <see cref="ColorKind.Default"/>.</summary>
    public int Value { get; }

    /// <summary>The terminal's default colour.</summary>
    public static Color Default { get; } = new(ColorKind.Default, 0);

    /// <summary>A palette colour.</summary>
    /// <param name="index">0–255.</param>
    public static Color Palette(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 255);
        return new(ColorKind.Palette, index);
    }

    /// <summary>A 24-bit colour.</summary>
    public static Color Rgb(byte r, byte g, byte b) => new(ColorKind.Rgb, (r << 16) | (g << 8) | b);

    /// <summary>An ANSI colour as its palette index.</summary>
    public static implicit operator Color(AnsiColor color) => Palette((int)color);

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        ColorKind.Default => "default",
        ColorKind.Palette when Value < 16 => $"{(AnsiColor)Value} (palette {Value})",
        ColorKind.Palette => $"palette {Value}",
        _ => $"#{Value:X6}",
    };
}
