namespace Puppetty.Tests;

public class QuiescenceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly SettleOptions Options = new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(75),
        EscapeQuietPeriod = TimeSpan.FromMilliseconds(250),
    };

    private static DateTimeOffset At(int ms) => T0 + TimeSpan.FromMilliseconds(ms);

    [Test]
    public void SettlesAfterQuietPeriodSinceLastOutput()
    {
        var q = new Quiescence(Options, T0);
        q.OnOutput(At(10));
        Assert.That(q.Remaining(At(50), false), Is.EqualTo(TimeSpan.FromMilliseconds(35)));
        Assert.That(q.Remaining(At(85), false), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void NewOutputRestartsTheQuietPeriod()
    {
        var q = new Quiescence(Options, T0);
        q.OnOutput(At(10));
        q.OnOutput(At(80));
        Assert.That(q.Remaining(At(100), false), Is.EqualTo(TimeSpan.FromMilliseconds(55)));
    }

    [Test]
    public void QuietPeriodCountsFromInputWhenTheAppIsSilent()
    {
        var q = new Quiescence(Options, T0);
        q.OnOutput(At(0));
        q.OnInput(At(1000), bareEscape: false);
        Assert.That(q.Remaining(At(1010), false), Is.EqualTo(TimeSpan.FromMilliseconds(65)));
    }

    [Test]
    public void BareEscapeUsesTheLongerQuietPeriod()
    {
        var q = new Quiescence(Options, T0);
        q.OnInput(At(0), bareEscape: true);
        Assert.That(q.Remaining(At(100), false), Is.EqualTo(TimeSpan.FromMilliseconds(150)));

        q.OnInput(At(300), bareEscape: false);
        Assert.That(q.Remaining(At(300), false), Is.EqualTo(TimeSpan.FromMilliseconds(75)));
    }

    [Test]
    public void NeverSettledInsideSynchronizedOutput()
    {
        var q = new Quiescence(Options, T0);
        Assert.That(q.Remaining(At(10_000), inSynchronizedOutput: true), Is.Null);
        Assert.That(q.Remaining(At(10_000), inSynchronizedOutput: false), Is.EqualTo(TimeSpan.Zero));
    }
}
