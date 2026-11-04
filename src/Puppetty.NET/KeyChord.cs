namespace Puppetty;

/// <summary>
/// One key press with its modifiers, either a named <see cref="Puppetty.Key"/> or a single character.
/// </summary>
/// <remarks>
/// Parse chords with <see cref="Parse"/>: <c>"Enter"</c>, <c>"Ctrl+C"</c>, <c>"Alt+F"</c>,
/// <c>"Shift+Tab"</c>, <c>"F5"</c>, <c>"Up"</c>, <c>"a"</c>, <c>"Ctrl++"</c>.
/// Names and modifiers are case-insensitive.
/// </remarks>
public readonly record struct KeyChord
{
    private KeyChord(Key? key, char? character, KeyModifiers modifiers)
    {
        Key = key;
        Character = character;
        Modifiers = modifiers;
    }

    /// <summary>The named key, or null when this chord is a character.</summary>
    public Key? Key { get; }

    /// <summary>The character, or null when this chord is a named key.</summary>
    public char? Character { get; }

    /// <summary>Modifiers held with the key.</summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>Creates a chord for a named key.</summary>
    public static KeyChord Of(Key key, KeyModifiers modifiers = KeyModifiers.None) => new(key, null, modifiers);

    /// <summary>Creates a chord for a character key.</summary>
    public static KeyChord Of(char character, KeyModifiers modifiers = KeyModifiers.None) => new(null, character, modifiers);

    /// <summary>Parses a chord such as <c>"Ctrl+Shift+Tab"</c>.</summary>
    /// <exception cref="FormatException">The text is not a recognised chord.</exception>
    public static KeyChord Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            throw new FormatException("Empty key chord.");

        // The key is whatever follows the last '+', except that a trailing '+' is itself the key
        // ("Ctrl++"), and a lone "+" is the plus key.
        string keyPart;
        string modifierPart;
        if (text == "+")
        {
            keyPart = "+";
            modifierPart = "";
        }
        else if (text.EndsWith("++", StringComparison.Ordinal))
        {
            keyPart = "+";
            modifierPart = text[..^2];
        }
        else
        {
            var split = text.LastIndexOf('+');
            keyPart = text[(split + 1)..];
            modifierPart = split < 0 ? "" : text[..split];
        }

        var modifiers = KeyModifiers.None;
        if (modifierPart.Length > 0)
        {
            foreach (var name in modifierPart.Split('+'))
                modifiers |= ParseModifier(name.Trim(), text);
        }

        // A lone space is the space key; otherwise ignore padding around the name.
        if (keyPart.Trim().Length > 0)
            keyPart = keyPart.Trim();
        if (keyPart.Length == 1)
            return Of(keyPart[0], modifiers);
        if (TryParseKey(keyPart, out var key))
            return Of(key, modifiers);
        throw new FormatException($"Unknown key '{keyPart}' in chord '{text}'.");
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var prefix = "";
        if (Modifiers.HasFlag(KeyModifiers.Control)) prefix += "Ctrl+";
        if (Modifiers.HasFlag(KeyModifiers.Alt)) prefix += "Alt+";
        if (Modifiers.HasFlag(KeyModifiers.Shift)) prefix += "Shift+";
        return prefix + (Key?.ToString() ?? Character.ToString());
    }

    private static KeyModifiers ParseModifier(string name, string chord) => name.ToLowerInvariant() switch
    {
        "ctrl" or "control" or "ctl" => KeyModifiers.Control,
        "alt" or "meta" or "option" or "opt" => KeyModifiers.Alt,
        "shift" => KeyModifiers.Shift,
        _ => throw new FormatException($"Unknown modifier '{name}' in chord '{chord}'."),
    };

    private static bool TryParseKey(string name, out Key key)
    {
        switch (name.ToLowerInvariant())
        {
            case "return" or "enter" or "cr": key = Puppetty.Key.Enter; return true;
            case "esc" or "escape": key = Puppetty.Key.Escape; return true;
            case "bs" or "backspace": key = Puppetty.Key.Backspace; return true;
            case "del" or "delete": key = Puppetty.Key.Delete; return true;
            case "ins" or "insert": key = Puppetty.Key.Insert; return true;
            case "up" or "arrowup" or "uparrow": key = Puppetty.Key.Up; return true;
            case "down" or "arrowdown" or "downarrow": key = Puppetty.Key.Down; return true;
            case "left" or "arrowleft" or "leftarrow": key = Puppetty.Key.Left; return true;
            case "right" or "arrowright" or "rightarrow": key = Puppetty.Key.Right; return true;
            case "pgup" or "pageup": key = Puppetty.Key.PageUp; return true;
            case "pgdn" or "pgdown" or "pagedown": key = Puppetty.Key.PageDown; return true;
        }
        // Enum.TryParse also accepts numbers ("12"), which are not key names.
        key = default;
        return char.IsLetter(name[0]) && Enum.TryParse(name, ignoreCase: true, out key) && Enum.IsDefined(key);
    }
}
