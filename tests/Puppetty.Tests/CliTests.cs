using System.Diagnostics;

namespace Puppetty.Tests;

/// <summary>
/// Runs the real `puppetty` CLI as a child process, against a private daemon (unique PUPPETTY_PIPE)
/// that the first command starts and the teardown stops.
/// </summary>
[Category("E2E")]
[NonParallelizable]
public class CliTests
{
    private static readonly string DotNet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    private static readonly string CliDll = Path.Combine(AppContext.BaseDirectory, "Puppetty.dll");
    private static readonly string FixtureDll = Path.Combine(AppContext.BaseDirectory, "Puppetty.Fixture.dll");

    private string _pipe = null!;

    [SetUp]
    public void UsePrivateDaemon() => _pipe = $"puppetty-test-{Guid.NewGuid():N}";

    [TearDown]
    public async Task StopDaemon() => await RunAsync("stop");

    private async Task<(int ExitCode, string Out, string Err)> RunAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo(DotNet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        startInfo.ArgumentList.Add(CliDll);
        foreach (var a in args)
            startInfo.ArgumentList.Add(a);
        startInfo.Environment["PUPPETTY_PIPE"] = _pipe;

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, (await stdout).Replace("\r\n", "\n"), await stderr);
    }

    private async Task<string> StartEchoAsync()
    {
        var (code, output, error) = await RunAsync("start", "--cols", "60", "--rows", "8", "--", DotNet, FixtureDll, "echo");
        Assert.That(code, Is.EqualTo(0), error);
        return output.Trim();
    }

    [Test]
    public async Task SessionsPersistAcrossInvocations()
    {
        var id = await StartEchoAsync();
        Assert.That(id, Is.EqualTo("t1"));

        Assert.That((await RunAsync("type", "--id", id, "hello cli")).ExitCode, Is.EqualTo(0));
        Assert.That((await RunAsync("press", "--id", id, "Enter")).ExitCode, Is.EqualTo(0));
        Assert.That((await RunAsync("wait", "--id", id, "--text", "echo: hello cli", "--timeout", "5s")).ExitCode, Is.EqualTo(0));

        var snap = await RunAsync("snap", "--id", id);
        Assert.That(snap.Out, Does.StartWith("> hello cli\necho: hello cli\n>"));

        var annotated = await RunAsync("snap", "--id", id, "--format", "annotated");
        // On Unix, .NET's console also turns on application cursor keys, so the header may go on.
        Assert.That(annotated.Out, Does.StartWith("size 60x8 · cursor 2,2"));
        Assert.That(annotated.Out, Does.Contain("\n1│echo: hello cli\n"));

        var ls = await RunAsync("ls");
        Assert.That(ls.Out, Does.Match(@"^t1\t60x8\trunning \(pid \d+\)\t"));

        Assert.That((await RunAsync("close", "--id", id)).Out.Trim(), Is.EqualTo("killed"));
        Assert.That((await RunAsync("ls")).Out, Is.Empty);
    }

    [Test]
    public async Task CommandLineFormIsSplitLikeAShell()
    {
        var (code, output, error) = await RunAsync("start", "--cmd", $"\"{DotNet}\" \"{FixtureDll}\" exit 7");
        Assert.That(code, Is.EqualTo(0), error);
        var wait = await RunAsync("wait", "--id", output.Trim(), "--exit");
        Assert.That(wait.Out.Trim(), Is.EqualTo("7"));
    }

    [Test]
    public async Task FailuresExitNonZeroWithTheScreen()
    {
        var id = await StartEchoAsync();
        var (code, _, error) = await RunAsync("wait", "--id", id, "--text", "never", "--timeout", "300ms");
        Assert.That(code, Is.EqualTo(1));
        Assert.That(error, Does.Contain("Timed out").And.Contain("--- screen ---"));

        var missing = await RunAsync("snap", "--id", "t42");
        Assert.That(missing.ExitCode, Is.EqualTo(1));
        Assert.That(missing.Err, Does.Contain("No session 't42'"));
    }

    [Test]
    public async Task AttachMirrorsTheSessionLive()
    {
        var id = await StartEchoAsync();

        // Watch the session through `puppetty attach`, itself running in a Puppetty terminal.
        await using var viewer = await Terminal.LaunchAsync(DotNet, new LaunchOptions
        {
            Args = [CliDll, "attach", "--id", id],
            Cols = 70, Rows = 10,
            Env = { ["PUPPETTY_PIPE"] = _pipe },
        });
        await viewer.WaitForTextAsync(">");

        await RunAsync("type", "--id", id, "mirrored\n");
        await viewer.WaitForTextAsync("echo: mirrored");

        await RunAsync("type", "--id", id, "quit\n");
        await viewer.WaitForTextAsync($"session {id} exited with code 0");
        Assert.That(await viewer.WaitForExitAsync(), Is.EqualTo(0));
    }
}
