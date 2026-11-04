namespace Puppetty.Tests;

[Category("E2E")]
public class FixtureTests
{
    [Test]
    public async Task EchoesTypedLines()
    {
        await using var term = await Fixture.LaunchAsync("echo");
        await term.WaitForTextAsync("> ");

        await term.TypeAsync("hello world");
        await term.PressAsync("Enter");
        await term.WaitForTextAsync("echo: hello world");

        await term.TypeAsync("quit\n");
        Assert.That(await term.WaitForExitAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task NavigatesMenuWithArrowKeys()
    {
        await using var term = await Fixture.LaunchAsync("menu");
        await term.WaitForTextAsync("> Apple");

        await term.PressAsync(Key.Down);
        await term.WaitForTextAsync("> Banana");
        await term.PressAsync("Down");
        await term.WaitForTextAsync("> Cherry");
        await term.PressAsync("Up");
        var screen = await term.WaitForTextAsync("> Banana");
        Assert.That(screen.Line(1), Is.EqualTo("  Apple"));

        await term.PressAsync("Enter");
        await term.WaitForTextAsync("Selected: Banana");
        Assert.That(await term.WaitForExitAsync(), Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(3)]
    [TestCase(42)]
    public async Task ReportsExitCode(int code)
    {
        await using var term = await Fixture.LaunchAsync("exit", code.ToString());
        Assert.That(await term.WaitForExitAsync(), Is.EqualTo(code));
        Assert.That(term.Screen.Contains($"exiting with {code}"), Is.True, term.Screen.GetText());
    }

    [Test]
    public async Task WaitForTextFailsFastWhenOutputEnds()
    {
        await using var term = await Fixture.LaunchAsync("exit", "0");
        var ex = Assert.ThrowsAsync<TerminalTimeoutException>(() => term.WaitForTextAsync("never printed", TimeSpan.FromSeconds(30)));
        Assert.That(ex!.Message, Does.Contain("Output ended"));
        Assert.That(ex.Screen.Contains("exiting with 0"), Is.True);
    }

    [Test]
    public async Task WaitForTextTimesOutWithScreenInMessage()
    {
        await using var term = await Fixture.LaunchAsync("echo");
        await term.WaitForTextAsync("> ");
        var ex = Assert.ThrowsAsync<TerminalTimeoutException>(() => term.WaitForTextAsync("never printed", TimeSpan.FromMilliseconds(300)));
        Assert.That(ex!.Message, Does.Contain("Timed out").And.Contain("--- screen ---\n>"));
    }
}
