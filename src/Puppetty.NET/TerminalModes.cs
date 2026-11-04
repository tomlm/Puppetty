namespace Puppetty;

/// <summary>Which mouse events the application asked to receive.</summary>
public enum MouseTracking
{
    /// <summary>None: clicks are not reported.</summary>
    None,
    /// <summary>Button presses only (mode 9).</summary>
    Press,
    /// <summary>Presses and releases (mode 1000).</summary>
    PressRelease,
    /// <summary>Presses, releases and drags (mode 1002).</summary>
    Drag,
    /// <summary>All events including plain motion (mode 1003).</summary>
    Any,
}

/// <summary>Input-relevant modes the application has set, captured with a <see cref="Screen"/>.</summary>
/// <param name="ApplicationCursorKeys">DECCKM: arrows send <c>ESC O A</c> instead of <c>ESC [ A</c>.</param>
/// <param name="ApplicationKeypad">DECKPAM: keypad sends application sequences.</param>
/// <param name="BracketedPaste">Mode 2004: pasted text is wrapped in <c>ESC [200~</c> … <c>ESC [201~</c>.</param>
/// <param name="Mouse">Which mouse events are reported.</param>
/// <param name="MouseEncoding">How mouse events are encoded: <c>default</c>, <c>utf8</c>, <c>sgr</c>, <c>urxvt</c> or <c>sgr-pixels</c>.</param>
/// <param name="FocusReporting">Mode 1004: focus in/out is reported.</param>
/// <param name="SynchronizedOutput">Mode 2026: the application is in the middle of drawing a frame.</param>
/// <param name="KittyKeyboardFlags">Kitty keyboard protocol flags; 0 when not negotiated.</param>
public sealed record TerminalModes(
    bool ApplicationCursorKeys,
    bool ApplicationKeypad,
    bool BracketedPaste,
    MouseTracking Mouse,
    string MouseEncoding,
    bool FocusReporting,
    bool SynchronizedOutput,
    int KittyKeyboardFlags)
{
    internal static TerminalModes Capture(XTerm.Terminal emulator) => new(
        emulator.ApplicationCursorKeys,
        emulator.ApplicationKeypad,
        emulator.BracketedPasteMode,
        emulator.MouseTrackingMode switch
        {
            XTerm.Input.MouseTrackingMode.X10 => MouseTracking.Press,
            XTerm.Input.MouseTrackingMode.VT200 => MouseTracking.PressRelease,
            XTerm.Input.MouseTrackingMode.ButtonEvent => MouseTracking.Drag,
            XTerm.Input.MouseTrackingMode.AnyEvent => MouseTracking.Any,
            _ => MouseTracking.None,
        },
        emulator.MouseEncoding switch
        {
            XTerm.Input.MouseEncoding.Utf8 => "utf8",
            XTerm.Input.MouseEncoding.SGR => "sgr",
            XTerm.Input.MouseEncoding.URXVT => "urxvt",
            XTerm.Input.MouseEncoding.SGRPixels => "sgr-pixels",
            _ => "default",
        },
        emulator.SendFocusEvents,
        emulator.SynchronizedOutput,
        emulator.KittyKeyboardActive ? (int)emulator.KittyKeyboardState.Flags : 0);
}
