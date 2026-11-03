namespace Puppetty.Tests;

/// <summary>
/// What actually reaches the application for mouse, paste, Kitty keys and resize. The raw fixture
/// echoes every input chunk back as "got: ..." with ESC shown as \e.
/// </summary>
[Category("E2E")]
public class InputModeTests
{
    private static async Task<Terminal> RawAsync(params string[] modes)
    {
        var term = await Fixture.LaunchAsync(["raw", .. modes]);
        await term.WaitForTextAsync("raw ready");
        return term;
    }

    [Test]
    public async Task ClickIsEncodedForSgrMouseMode()
    {
        await using var term = await RawAsync("?1000h", "?1006h");
        Assert.That(term.Screen.Modes.Mouse, Is.EqualTo(MouseTracking.PressRelease));

        Assert.That(await term.ClickAsync(row: 2, col: 5), Is.True);
        // SGR coordinates are 1-based: column 6, row 3. Press is 'M', release 'm'.
        await term.WaitForScreenAsync(s => Received(s) == @"\e[<0;6;3M\e[<0;6;3m", "SGR press and release");
    }

    [Test]
    public async Task ClickSendsNothingWithoutMouseMode()
    {
        await using var term = await RawAsync();
        Assert.That(await term.ClickAsync(1, 1), Is.False);
        Assert.That(term.Screen.Contains("got:"), Is.False);
    }

    [Test]
    public async Task LocatorClickTargetsTheMatch()
    {
        await using var term = await RawAsync("?1000h", "?1006h");
        // "raw ready" is on row 0; "ready" starts at column 4.
        Assert.That(await term.GetByText("ready").ClickAsync(), Is.True);
        await term.WaitForTextAsync(@"\e[<0;5;1M");
    }

    /// <summary>All input the raw fixture has reported, chunks joined (ConPTY may split one write).</summary>
    private static string Received(Screen s) =>
        string.Concat(s.Lines.Where(l => l.StartsWith("got: ", StringComparison.Ordinal)).Select(l => l["got: ".Length..]));

    [Test]
    public async Task PasteIsBracketedWhenTheAppAsks()
    {
        await using var term = await RawAsync("?2004h");
        await term.PasteAsync("hi there");
        await term.WaitForScreenAsync(s => Received(s) == @"\e[200~hi there\e[201~", "bracketed paste");
    }

    [Test]
    public async Task PasteIsPlainOtherwise()
    {
        await using var term = await RawAsync();
        await term.PasteAsync("hi there");
        await term.WaitForScreenAsync(s => Received(s) == "hi there", "plain paste");
    }

    [Test]
    public async Task KeysUseKittyProtocolOnceNegotiated()
    {
        // CSI > 1 u pushes "disambiguate escape codes".
        await using var term = await RawAsync(">1u");
        await term.WaitForScreenAsync(s => s.Modes.KittyKeyboardFlags == 1, "kitty keyboard flags");

        await term.PressAsync("Escape");
        await term.WaitForTextAsync(@"got: \e[27u");
        await term.PressAsync("Ctrl+c");
        await term.WaitForTextAsync(@"got: \e[99;5u");
    }

    [Test]
    public async Task ResizeReachesTheApplication()
    {
        await using var term = await Fixture.LaunchAsync("size");
        await term.WaitForTextAsync("size 80x24");

        await term.ResizeAsync(100, 30);
        Assert.That((term.Screen.Cols, term.Screen.Rows), Is.EqualTo((100, 30)));
        await term.PressAsync("x");
        await term.WaitForTextAsync("size 100x30");
    }

    [Test]
    public async Task ScrollbackKeepsLinesThatScrolledOff()
    {
        await using var term = await Terminal.LaunchAsync(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            new LaunchOptions { Args = [Path.Combine(AppContext.BaseDirectory, "Puppetty.Fixture.dll"), "echo"], Rows = 5 });
        await term.WaitForTextAsync("> ");
        for (var i = 1; i <= 6; i++)
        {
            await term.TypeAsync($"line {i}\n");
            await term.WaitForTextAsync($"echo: line {i}");
        }

        var scrollback = term.GetScrollback();
        Assert.That(scrollback, Does.Contain("> line 1"));
        Assert.That(scrollback, Does.Contain("echo: line 1"));
        Assert.That(term.Screen.Contains("echo: line 1"), Is.False, "line 1 should have scrolled off");
        Assert.That(term.GetScrollback(2), Has.Count.EqualTo(2));
    }
}
