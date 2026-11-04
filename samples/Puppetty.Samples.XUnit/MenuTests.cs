using Puppetty;
using Xunit;
using static Puppetty.Testing.Assertions;

namespace Puppetty.Samples.XUnit;

/// <summary>
/// A sample xUnit suite driving a small arrow-key menu app. Each test gets its own terminal; xUnit
/// creates a new class instance per test and disposes it afterwards.
/// </summary>
public sealed class MenuTests : IAsyncLifetime
{
    private Terminal _term = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var app = Path.Combine(AppContext.BaseDirectory, "Puppetty.Fixture.dll");
        _term = await Terminal.LaunchAsync("dotnet", new LaunchOptions { Args = [app, "menu"], Cols = 40, Rows = 10 });
        await Expect(_term.Line(0)).ToHaveTextAsync("Pick a fruit:");
    }

    public ValueTask DisposeAsync() => _term.DisposeAsync();

    [Fact]
    public async Task FirstItemIsSelectedAtStart()
    {
        await Expect(_term.GetByText("> Apple")).ToBeVisibleAsync();
        await Expect(_term.GetByText(">")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task StartScreenMatchesGolden()
    {
        // Golden file: __snapshots__/menu-start.puppetty-snap next to this file. Created on the
        // first local run; PUPPETTY_UPDATE_SNAPSHOTS=1 rewrites it.
        await Expect(_term).ToMatchSnapshotAsync("menu-start");
    }

    [Fact]
    public async Task ArrowKeysMoveTheSelection()
    {
        await _term.PressAsync(Key.Down, cancellationToken: Ct);
        await Expect(_term.Line(2)).ToHaveTextAsync("> Banana");

        await _term.PressAsync("Up", Ct);
        await Expect(_term.Line(1)).ToHaveTextAsync("> Apple");
    }

    [Fact]
    public async Task SelectionWrapsAround()
    {
        await _term.PressAsync("Up", Ct);
        await Expect(_term.GetByText("> Cherry")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EnterChoosesTheItemAndExits()
    {
        await _term.PressAsync("Down", Ct);
        await _term.PressAsync("Enter", Ct);

        await Expect(_term).ToContainTextAsync("Selected: Banana");
        Assert.Equal(1, await _term.WaitForExitAsync(cancellationToken: Ct));
    }
}
