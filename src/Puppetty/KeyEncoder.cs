using XTerm.Input;
using KeyEvent = XTerm.Options.KeyEvent;
using Emulator = XTerm.Terminal;
using XKey = XTerm.Input.Key;
using XMods = XTerm.Input.KeyModifiers;

namespace Puppetty;

/// <summary>
/// Turns a <see cref="KeyChord"/> into the bytes a real terminal would send, given the modes the
/// running application has set on the emulator. All key encoding lives here.
/// </summary>
/// <remarks>
/// VT encodings (DECCKM application cursor keys, DECKPAM, Ctrl/Alt rules) come from XTerm.NET's own
/// generators so Puppetty sends exactly what an XTerm.NET-based terminal would.
/// On Windows, ConPTY asks for win32-input-mode (<c>CSI ?9001h</c>) at startup; while it is set, keys
/// are sent as win32 INPUT_RECORD sequences. A bare Escape is always sent that way on Windows, because
/// ConPTY has no VT spelling for it that it won't wait to disambiguate.
/// </remarks>
internal static class KeyEncoder
{
    /// <summary>Encodes <paramref name="chord"/> for the emulator's current modes.</summary>
    /// <param name="chord">The key to press.</param>
    /// <param name="emulator">The emulator whose modes decide the encoding. Caller holds its lock.</param>
    /// <param name="conPty">True when the far side is Windows ConPTY.</param>
    public static string Encode(KeyChord chord, Emulator emulator, bool conPty)
    {
        // An application that negotiated the Kitty keyboard protocol reads CSI-u, not legacy VT, and
        // OpenConsole relays CSI-u input to it unchanged, so this comes before win32-input-mode.
        // A null result means the protocol says this press sends nothing.
        if (emulator.KittyKeyboardActive)
            return emulator.GenerateKittyKeyInput(ToKeyEvent(chord)) ?? string.Empty;

        if (conPty && UseWin32Input(chord, emulator))
        {
            var win32 = EncodeWin32(chord);
            if (win32.Length > 0)
                return win32;
        }

        var mods = ToXterm(chord.Modifiers);
        if (chord.Key is { } key)
            return key == Key.Space
                ? emulator.GenerateCharInput(' ', mods)
                : emulator.GenerateKeyInput(ToXterm(key), mods);
        return emulator.GenerateCharInput(chord.Character!.Value, mods);
    }

    private static bool UseWin32Input(KeyChord chord, Emulator emulator) =>
        emulator.Win32InputMode || (chord.Key == Key.Escape && chord.Modifiers == KeyModifiers.None);

    /// <summary>Key down + key up as win32-input-mode sequences, or "" if the key has no virtual key code.</summary>
    internal static string EncodeWin32(KeyChord chord)
    {
        ushort vk;
        char unicode;
        var state = Win32InputGenerator.ConvertModifiers(ToXterm(chord.Modifiers));

        if (chord.Key is { } key)
        {
            (vk, unicode, var enhanced) = key switch
            {
                Key.Enter => ((ushort)0x0D, '\r', false),
                Key.Tab => ((ushort)0x09, '\t', false),
                Key.Backspace => ((ushort)0x08, '\b', false),
                Key.Escape => ((ushort)0x1B, '\u001b', false),
                Key.Space => ((ushort)0x20, ' ', false),
                Key.PageUp => ((ushort)0x21, '\0', true),
                Key.PageDown => ((ushort)0x22, '\0', true),
                Key.End => ((ushort)0x23, '\0', true),
                Key.Home => ((ushort)0x24, '\0', true),
                Key.Left => ((ushort)0x25, '\0', true),
                Key.Up => ((ushort)0x26, '\0', true),
                Key.Right => ((ushort)0x27, '\0', true),
                Key.Down => ((ushort)0x28, '\0', true),
                Key.Insert => ((ushort)0x2D, '\0', true),
                Key.Delete => ((ushort)0x2E, '\0', true),
                >= Key.F1 and <= Key.F12 => ((ushort)(0x70 + (key - Key.F1)), '\0', false),
                _ => ((ushort)0, '\0', false),
            };
            if (enhanced)
                state |= Win32InputGenerator.ControlKeyState.EnhancedKey;
            if (key == Key.Space && chord.Modifiers.HasFlag(KeyModifiers.Control))
                unicode = '\0';
        }
        else
        {
            var c = chord.Character!.Value;
            vk = Win32InputGenerator.GetVirtualKeyCode(c);
            if (char.IsAsciiLetterUpper(c))
                state |= Win32InputGenerator.ControlKeyState.ShiftPressed;
            unicode = c;
            if (chord.Modifiers.HasFlag(KeyModifiers.Control) && char.IsAsciiLetter(c))
                unicode = (char)(char.ToUpperInvariant(c) - '@');
            else if (chord.Modifiers.HasFlag(KeyModifiers.Shift) && char.IsAsciiLetterLower(c))
                unicode = char.ToUpperInvariant(c);
        }

        if (vk == 0)
            return string.Empty;
        return Win32InputGenerator.GenerateKeyPress(vk, Win32InputGenerator.GetScanCode(vk), unicode, state);
    }

    /// <summary>A browser-style key event (DOM <c>key</c> / <c>code</c> names), as the Kitty encoder expects.</summary>
    private static KeyEvent ToKeyEvent(KeyChord chord)
    {
        var shift = chord.Modifiers.HasFlag(KeyModifiers.Shift);
        string keyName, code;
        if (chord.Key is { } key)
        {
            (keyName, code) = key switch
            {
                Key.Space => (" ", "Space"),
                Key.Up => ("ArrowUp", "ArrowUp"),
                Key.Down => ("ArrowDown", "ArrowDown"),
                Key.Left => ("ArrowLeft", "ArrowLeft"),
                Key.Right => ("ArrowRight", "ArrowRight"),
                _ => (key.ToString(), key.ToString()),
            };
        }
        else
        {
            var c = chord.Character!.Value;
            if (char.IsAsciiLetterUpper(c))
                shift = true;
            keyName = (shift && char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : c).ToString();
            code = char.IsAsciiLetter(c) ? $"Key{char.ToUpperInvariant(c)}"
                : char.IsAsciiDigit(c) ? $"Digit{c}"
                : "";
        }

        return new KeyEvent
        {
            Key = keyName,
            Code = code,
            CtrlKey = chord.Modifiers.HasFlag(KeyModifiers.Control),
            AltKey = chord.Modifiers.HasFlag(KeyModifiers.Alt),
            ShiftKey = shift,
        };
    }

    private static XMods ToXterm(KeyModifiers modifiers) => (XMods)(int)modifiers;

    private static XKey ToXterm(Key key) => key switch
    {
        Key.Enter => XKey.Enter,
        Key.Tab => XKey.Tab,
        Key.Backspace => XKey.Backspace,
        Key.Escape => XKey.Escape,
        Key.Space => XKey.Space,
        Key.Up => XKey.UpArrow,
        Key.Down => XKey.DownArrow,
        Key.Left => XKey.LeftArrow,
        Key.Right => XKey.RightArrow,
        Key.Home => XKey.Home,
        Key.End => XKey.End,
        Key.PageUp => XKey.PageUp,
        Key.PageDown => XKey.PageDown,
        Key.Insert => XKey.Insert,
        Key.Delete => XKey.Delete,
        >= Key.F1 and <= Key.F12 => XKey.F1 + (key - Key.F1),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}
