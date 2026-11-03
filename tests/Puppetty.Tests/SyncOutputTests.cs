namespace Puppetty.Tests;

[Category("E2E")]
public class SyncOutputTests
{
    // The fixture pauses 500 ms in the middle of a synchronized-output frame. Quiescence alone
    // (75 ms) would return with "drawing" on screen; honouring mode 2026 waits for "done".
    [Test]
    public async Task ActionsSettleAfterTheSynchronizedFrameEnds()
    {
        await using var term = await Fixture.LaunchAsync("sync");
        await term.WaitForTextAsync("ready");

        for (var frame = 1; frame <= 2; frame++)
        {
            await term.PressAsync("x");
            var screen = term.Screen;
            Assert.That(screen.Line(0), Is.EqualTo($"frame {frame}: done"), screen.GetText());
        }
    }
}
