namespace Puppetty.Tests;

[Category("E2E")]
public class PythonReplTests
{
    [Test]
    public async Task ComputesSixTimesSeven()
    {
        var python = FindPython() ?? throw new IgnoreException("python not found on PATH");

        await using var term = await Terminal.LaunchAsync(python, new LaunchOptions { Args = ["-i", "-q"] });
        await term.WaitForTextAsync(">>> ");

        await term.TypeAsync("print(6*7)");
        await term.PressAsync("Enter");
        var screen = await term.WaitForScreenAsync(s => s.Lines.Any(l => l.Trim() == "42"), "a line reading 42");
        Assert.That(screen.GetText(), Does.Contain("print(6*7)"));

        await term.TypeAsync("exit()\n");
        Assert.That(await term.WaitForExitAsync(), Is.EqualTo(0));
    }

    private static string? FindPython()
    {
        // On Windows "python3" is often the Microsoft Store stub, so prefer "python" there.
        string[] names = OperatingSystem.IsWindows() ? ["python.exe", "python3.exe"] : ["python3", "python"];
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (dir.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }
}
