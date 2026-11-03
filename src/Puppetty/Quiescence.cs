namespace Puppetty;

/// <summary>
/// Decides when the screen has settled. Pure state over timestamps, so it is unit-tested with a
/// fake clock; <see cref="Terminal"/> does the waiting.
/// </summary>
/// <remarks>
/// Settled means: the application is not inside a synchronized-output block (DEC mode 2026), and
/// nothing has happened (output received or input sent) for the quiet period. The quiet period is
/// longer after a bare Escape, because applications hold Escape back to tell it apart from Alt+key.
/// </remarks>
internal sealed class Quiescence
{
    private readonly SettleOptions _options;
    private DateTimeOffset _lastActivity;
    private TimeSpan _quiet;

    public Quiescence(SettleOptions options, DateTimeOffset now)
    {
        _options = options;
        _lastActivity = now;
        _quiet = options.QuietPeriod;
    }

    /// <summary>Output arrived from the application.</summary>
    public void OnOutput(DateTimeOffset now) => _lastActivity = now;

    /// <summary>Input was sent to the application.</summary>
    /// <param name="now">When it was sent.</param>
    /// <param name="bareEscape">True when the input ended with a bare Escape key.</param>
    public void OnInput(DateTimeOffset now, bool bareEscape)
    {
        _lastActivity = now;
        _quiet = bareEscape ? _options.EscapeQuietPeriod : _options.QuietPeriod;
    }

    /// <summary>
    /// How much longer to wait before the screen counts as settled: <see cref="TimeSpan.Zero"/> when
    /// settled now, or null when inside a synchronized-output block (wait for it to end).
    /// </summary>
    public TimeSpan? Remaining(DateTimeOffset now, bool inSynchronizedOutput)
    {
        if (inSynchronizedOutput)
            return null;
        var left = _quiet - (now - _lastActivity);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}
