namespace Puppetty;

/// <summary>
/// A wait did not succeed in time, or could not succeed because the program's output ended.
/// Carries the screen as it was at that moment.
/// </summary>
public sealed class TerminalTimeoutException : TimeoutException
{
    internal TerminalTimeoutException(string message, Screen screen)
        : base($"{message}\n--- screen ---\n{screen.GetText()}\n--------------")
    {
        Screen = screen;
    }

    /// <summary>The screen when the wait gave up.</summary>
    public Screen Screen { get; }
}
