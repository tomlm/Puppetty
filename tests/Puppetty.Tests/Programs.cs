namespace Puppetty.Tests;

/// <summary>Finds real programs on PATH for the smoke tests, which skip themselves when one is missing.</summary>
internal static class Programs
{
    public static string? Find(params string[] names)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var name in names)
        {
            var candidates = OperatingSystem.IsWindows() ? [name + ".exe", name + ".cmd", name] : new[] { name };
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                // Microsoft Store app-execution aliases (python3.exe, ...) are stubs, not the program.
                if (dir.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var candidate in candidates)
                {
                    var full = Path.Combine(dir, candidate);
                    if (File.Exists(full))
                        return full;
                }
            }
        }
        return null;
    }
}
