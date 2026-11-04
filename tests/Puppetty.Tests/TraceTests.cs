using Puppetty.Testing;
using static Puppetty.Testing.Assertions;

namespace Puppetty.Tests;

[Category("E2E")]
[NonParallelizable]
public class TraceTests
{
    private string _dir = null!;
    private string? _savedCi;

    [SetUp]
    public void UseTempDirectories()
    {
        _dir = Path.Combine(Path.GetTempPath(), "puppetty-tests", Guid.NewGuid().ToString("N"));
        Assertions.ArtifactsDirectory = Path.Combine(_dir, "artifacts");
        Assertions.SnapshotDirectory = Path.Combine(_dir, "snapshots");
        // A missing golden fails under CI; these tests create goldens on purpose.
        _savedCi = Environment.GetEnvironmentVariable("CI");
        Environment.SetEnvironmentVariable("CI", null);
    }

    [TearDown]
    public void Restore()
    {
        Environment.SetEnvironmentVariable("CI", _savedCi);
        Assertions.SnapshotDirectory = null;
        Assertions.ArtifactsDirectory = Path.Combine(Environment.CurrentDirectory, "puppetty-artifacts");
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Test]
    public async Task RecordsSavesLoadsAndReplays()
    {
        await using var term = await Fixture.LaunchAsync("echo");
        await term.WaitForTextAsync("> ");
        var beforeTyping = DateTimeOffset.UtcNow;
        await term.TypeAsync("hello\n");
        await term.WaitForTextAsync("echo: hello");

        var trace = term.GetTrace();
        Assert.That(trace.Events.Any(e => e.Kind == TraceEventKind.Input && System.Text.Encoding.UTF8.GetString(e.Data) == "hello\r"));
        Assert.That(trace.Events.Any(e => e.Kind == TraceEventKind.Output));
        Assert.That(trace.Events.Any(e => e.Kind == TraceEventKind.Settled && e.Note!.StartsWith("quiet")), trace.ToTimeline());

        var path = Path.Combine(_dir, "t" + Trace.Extension);
        trace.Save(path);
        var loaded = Trace.Load(path);
        Assert.That(loaded.Events, Has.Count.EqualTo(trace.Events.Count));
        Assert.That(loaded.Command, Is.EqualTo(trace.Command));

        Assert.That(loaded.Replay().GetText(), Is.EqualTo(term.Screen.GetText()));
        var early = loaded.Replay(until: beforeTyping - trace.StartedAt);
        Assert.That(early.Contains("echo: hello"), Is.False);
        Assert.That(early.Contains("> "), Is.True);
        Assert.That(loaded.ToTimeline(), Does.Contain("in   hello\\r"));
    }

    [Test]
    public async Task FailedExpectationSavesTheTrace()
    {
        await using var term = await Fixture.LaunchAsync("echo");
        await term.WaitForTextAsync("> ");

        var ex = Assert.ThrowsAsync<ExpectationException>(() => Expect(term.GetByText("never"), TimeSpan.FromMilliseconds(200)).ToBeVisibleAsync());
        var line = ex!.Message.Split('\n').Single(l => l.StartsWith("Trace: "));
        var path = line["Trace: ".Length..];
        Assert.That(File.Exists(path), path);
        Assert.That(Trace.Load(path).Replay().Contains("> "), Is.True);
    }

    [Test]
    public async Task SnapshotIsCreatedThenMatchedThenDiffed()
    {
        await using var term = await Fixture.LaunchAsync("menu");
        await Expect(term.GetByText("> Apple")).ToBeVisibleAsync();

        await Expect(term).ToMatchSnapshotAsync("menu");
        var golden = Path.Combine(Assertions.SnapshotDirectory!, "menu" + Snapshot.Extension);
        Assert.That(File.Exists(golden), Is.True, "first run creates the golden");

        await Expect(term).ToMatchSnapshotAsync("menu");

        await term.PressAsync("Down");
        var ex = Assert.ThrowsAsync<ExpectationException>(() => Expect(term, TimeSpan.FromMilliseconds(300)).ToMatchSnapshotAsync("menu"));
        Assert.That(ex!.Message, Does.Contain("Screen does not match snapshot 'menu'"));
        Assert.That(ex.Message, Does.Contain("> Apple").And.Contain("> Banana"));
        Assert.That(ex.Message, Does.Contain("Actual snapshot: ").And.Contain("Trace: "));
        Assert.That(Directory.GetFiles(Assertions.ArtifactsDirectory, "*.actual" + Snapshot.Extension), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task MissingSnapshotFailsUnderCi()
    {
        Environment.SetEnvironmentVariable("CI", "true");
        await using var term = await Fixture.LaunchAsync("menu");
        var ex = Assert.ThrowsAsync<ExpectationException>(() => Expect(term).ToMatchSnapshotAsync("absent"));
        Assert.That(ex!.Message, Does.Contain("does not exist"));
    }
}
