using System.Text.RegularExpressions;
using Puppetty.Testing;
using static Puppetty.Testing.Assertions;

namespace Puppetty.Tests;

[Category("E2E")]
public class ExpectTests
{
    [Test]
    public async Task ChecksCellColoursAndAttributes()
    {
        await using var term = await Fixture.LaunchAsync("styles");

        await Expect(term.Cell(0, 0)).ToHaveForegroundAsync(AnsiColor.Red);
        await Expect(term.Cell(0, 0)).ToHaveTextAsync("E");
        await Expect(term.Cell(0, 6)).ToHaveForegroundAsync(Color.Default);
        await Expect(term.Cell(0, 6)).Not.ToHaveForegroundAsync(AnsiColor.Red);
        await Expect(term.Cell(1, 0)).ToHaveAttributesAsync(CellAttributes.Bold | CellAttributes.Underline);
        await Expect(term.Cell(1, 5)).ToHaveAttributesAsync(CellAttributes.Inverse);
        await Expect(term.Cell(2, 0)).ToHaveForegroundAsync(Color.Rgb(255, 128, 0));
        await Expect(term.Cell(2, 4)).ToHaveBackgroundAsync(Color.Palette(200));
    }

    [Test]
    public async Task LocatesTextAndRegions()
    {
        await using var term = await Fixture.LaunchAsync("styles");

        await Expect(term.GetByText("ERROR")).ToBeVisibleAsync();
        await Expect(term.GetByText(new Regex(@"B[A-Z]"))).ToHaveTextAsync("BG");
        await Expect(term.Line(0)).ToHaveTextAsync("ERROR plain");
        await Expect(term.Line(3)).ToContainTextAsync("ok");
        await Expect(term.Region(1, 0, 4, 1)).ToHaveTextAsync("Bold");
        await Expect(term.GetByText("missing")).Not.ToBeVisibleAsync();
        await Expect(term).ToContainTextAsync("日本");

        var ok = await term.GetByText("ok").WaitForAsync();
        Assert.That((ok.Row, ok.Col), Is.EqualTo((3, 5)));
    }

    [Test]
    public async Task RetriesUntilTheScreenChanges()
    {
        await using var term = await Fixture.LaunchAsync("delayed", "700", "ready now");
        await Expect(term.GetByText("waiting")).ToBeVisibleAsync();
        Assert.That(term.GetByText("ready now").IsVisible, Is.False);

        await Expect(term.GetByText("ready now")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FailureExplainsExpectedAndActual()
    {
        await using var term = await Fixture.LaunchAsync("styles");
        await Expect(term.GetByText("ERROR")).ToBeVisibleAsync();

        var ex = Assert.ThrowsAsync<ExpectationException>(() =>
            Expect(term.Cell(0, 0), TimeSpan.FromMilliseconds(300)).ToHaveForegroundAsync(AnsiColor.Green));
        Assert.That(ex!.Message, Does.Contain("Expected cell (row 0, col 0) to have foreground Green (palette 2)"));
        Assert.That(ex.Message, Does.Contain("Actual: Red (palette 1)"));
        Assert.That(ex.Message, Does.Contain("ERROR plain"));
    }

    [Test]
    public async Task NegatedAssertionWaitsForTextToGo()
    {
        await using var term = await Fixture.LaunchAsync("menu");
        await Expect(term.GetByText("> Apple")).ToBeVisibleAsync();

        await term.PressAsync("Down");
        await Expect(term.GetByText("> Apple")).Not.ToBeVisibleAsync();
        await Expect(term.GetByText(">")).ToHaveCountAsync(1);
    }
}
