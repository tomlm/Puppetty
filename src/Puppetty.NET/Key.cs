namespace Puppetty;

/// <summary>Named (non-character) keys that <see cref="Terminal.PressAsync(Key, KeyModifiers, CancellationToken)"/> can press.</summary>
public enum Key
{
    /// <summary>Enter / Return.</summary>
    Enter,
    /// <summary>Tab.</summary>
    Tab,
    /// <summary>Backspace.</summary>
    Backspace,
    /// <summary>Escape.</summary>
    Escape,
    /// <summary>Space bar.</summary>
    Space,
    /// <summary>Up arrow.</summary>
    Up,
    /// <summary>Down arrow.</summary>
    Down,
    /// <summary>Left arrow.</summary>
    Left,
    /// <summary>Right arrow.</summary>
    Right,
    /// <summary>Home.</summary>
    Home,
    /// <summary>End.</summary>
    End,
    /// <summary>Page Up.</summary>
    PageUp,
    /// <summary>Page Down.</summary>
    PageDown,
    /// <summary>Insert.</summary>
    Insert,
    /// <summary>Delete (forward delete).</summary>
    Delete,
    /// <summary>F1.</summary>
    F1,
    /// <summary>F2.</summary>
    F2,
    /// <summary>F3.</summary>
    F3,
    /// <summary>F4.</summary>
    F4,
    /// <summary>F5.</summary>
    F5,
    /// <summary>F6.</summary>
    F6,
    /// <summary>F7.</summary>
    F7,
    /// <summary>F8.</summary>
    F8,
    /// <summary>F9.</summary>
    F9,
    /// <summary>F10.</summary>
    F10,
    /// <summary>F11.</summary>
    F11,
    /// <summary>F12.</summary>
    F12,
}

/// <summary>Modifier keys held during a key press.</summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>No modifiers.</summary>
    None = 0,
    /// <summary>Shift.</summary>
    Shift = 1,
    /// <summary>Alt (Option on macOS keyboards).</summary>
    Alt = 2,
    /// <summary>Control.</summary>
    Control = 4,
}
