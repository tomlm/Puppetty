using static Puppetty.Testing.Assertions;

namespace Puppetty.Tests;

/// <summary>Real programs, when installed. Each test skips itself if its program is missing.</summary>
[Category("Smoke")]
public class SmokeTests
{
    private static string Require(params string[] names) =>
        Programs.Find(names) ?? throw new IgnoreException($"{names[0]} not found on PATH");

    [Test]
    public async Task Vim()
    {
        var vim = Require("vim");
        await using var term = await Terminal.LaunchAsync(vim, new LaunchOptions { Args = ["-u", "NONE", "-N"] });
        await Expect(term).ToBeOnAlternateScreenAsync();

        await term.PressAsync("i");
        await term.TypeAsync("one\ntwo");
        await term.PressAsync("Escape");
        await Expect(term.Line(0)).ToHaveTextAsync("one");
        await Expect(term.Line(1)).ToHaveTextAsync("two");

        await term.PressAsync(new[] { "g", "g", "d", "d" }.Select(KeyChord.Parse));
        await Expect(term.Line(0)).ToHaveTextAsync("two");

        await term.TypeAsync(":q!\n");
        Assert.That(await term.WaitForExitAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Less()
    {
        var less = Require("less");
        var file = Path.Combine(Path.GetTempPath(), $"puppetty-less-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(file, Enumerable.Range(1, 200).Select(i => $"line {i}"));
        try
        {
            await using var term = await Terminal.LaunchAsync(less, new LaunchOptions { Args = [file], Env = { ["LESS"] = "" } });
            await Expect(term.Line(0)).ToHaveTextAsync("line 1");

            await term.PressAsync("G");
            await Expect(term.GetByText("line 200")).ToBeVisibleAsync();
            await term.PressAsync("g");
            await Expect(term.Line(0)).ToHaveTextAsync("line 1");

            await term.PressAsync("q");
            Assert.That(await term.WaitForExitAsync(), Is.EqualTo(0));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task Top()
    {
        var htop = Programs.Find("htop");
        var top = htop ?? Require("top");
        await using var term = await Terminal.LaunchAsync(top, new LaunchOptions { Cols = 120, Rows = 30 });
        // htop draws a function-key bar; top a load-average summary.
        await Expect(term.GetByText(htop is not null ? "Quit" : "load average")).ToBeVisibleAsync();

        await term.PressAsync(htop is not null ? "F10" : "q");
        Assert.That(await term.WaitForExitAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task PowerShell()
    {
        var pwsh = Require("pwsh");
        await using var term = await Terminal.LaunchAsync(pwsh, new LaunchOptions { Args = ["-NoLogo", "-NoProfile"], Cols = 100 });
        await term.WaitForTextAsync("PS ", TimeSpan.FromSeconds(30));

        await term.TypeAsync("6*7\n");
        await term.WaitForScreenAsync(s => s.Lines.Any(l => l.Trim() == "42"), "a line reading 42");

        await term.TypeAsync("exit 3\n");
        Assert.That(await term.WaitForExitAsync(TimeSpan.FromSeconds(30)), Is.EqualTo(3));
    }
}
