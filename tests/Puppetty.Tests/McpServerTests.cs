using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Puppetty.Tests;

/// <summary>Runs the real MCP server over stdio and drives it with the SDK client, as an agent would.</summary>
[Category("E2E")]
public class McpServerTests
{
    private static readonly string DotNet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    private static readonly string FixtureDll = Path.Combine(AppContext.BaseDirectory, "Puppetty.Fixture.dll");

    private McpClient _client = null!;

    [SetUp]
    public async Task StartServer()
    {
        _client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "puppetty",
            Command = DotNet,
            Arguments = [Path.Combine(AppContext.BaseDirectory, "Puppetty.Mcp.dll")],
        }));
    }

    [TearDown]
    public async Task StopServer() => await _client.DisposeAsync();

    private async Task<string> CallAsync(string tool, Dictionary<string, object?> args)
    {
        var result = await _client.CallToolAsync(tool, args);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        Assert.That(result.IsError, Is.Not.True, text);
        return text;
    }

    private static string SessionId(string launchResult) => launchResult.Split(' ', 3)[1];

    [Test]
    public async Task ListsTheTerminalTools()
    {
        var tools = (await _client.ListToolsAsync()).Select(t => t.Name).ToList();
        Assert.That(tools, Is.SupersetOf(new[]
        {
            "terminal_launch", "terminal_type", "terminal_press", "terminal_paste", "terminal_click", "terminal_wait_for",
            "terminal_screenshot", "terminal_read_region", "terminal_scrollback", "terminal_resize", "terminal_list", "terminal_close",
        }));
    }

    [Test]
    public async Task DrivesTheMenuFixture()
    {
        var launched = await CallAsync("terminal_launch", new() { ["command"] = DotNet, ["args"] = new[] { FixtureDll, "menu" } });
        Assert.That(launched, Does.StartWith("session t1 (pid "));
        var id = SessionId(launched);

        // Actions return an annotated screenshot by default.
        var afterDown = await CallAsync("terminal_press", new() { ["session"] = id, ["keys"] = new[] { "Down" } });
        Assert.That(afterDown, Does.StartWith("size 80x24 · cursor"));
        Assert.That(afterDown, Does.Contain("2│> Banana"));

        Assert.That(await CallAsync("terminal_list", new()), Does.Contain($"{id}  80x24  running"));

        await CallAsync("terminal_press", new() { ["session"] = id, ["keys"] = new[] { "Enter" } });
        var exited = await CallAsync("terminal_wait_for", new() { ["session"] = id, ["until"] = "exit", ["format"] = "text" });
        Assert.That(exited, Does.StartWith("exited with code 1"));
        Assert.That(exited, Does.Contain("Selected: Banana"));

        Assert.That(await CallAsync("terminal_read_region", new() { ["session"] = id, ["row"] = 0, ["col"] = 0, ["width"] = 4, ["height"] = 1 }), Is.EqualTo("Pick"));
        Assert.That(await CallAsync("terminal_close", new() { ["session"] = id }), Is.EqualTo($"closed {id}; it had exited with code 1"));
    }

    [Test]
    public async Task ReportsErrorsWithTheirMessage()
    {
        var result = await _client.CallToolAsync("terminal_screenshot", new Dictionary<string, object?> { ["session"] = "t99" });
        Assert.That(result.IsError, Is.True);
        Assert.That(string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text)), Does.Contain("No session 't99'"));

        var launched = await CallAsync("terminal_launch", new() { ["command"] = DotNet, ["args"] = new[] { FixtureDll, "echo" } });
        var timeout = await _client.CallToolAsync("terminal_wait_for", new Dictionary<string, object?>
        {
            ["session"] = SessionId(launched), ["text"] = "never", ["timeoutSeconds"] = 0.3,
        });
        Assert.That(timeout.IsError, Is.True);
        Assert.That(string.Concat(timeout.Content.OfType<TextContentBlock>().Select(b => b.Text)), Does.Contain("Timed out").And.Contain("--- screen ---"));
    }

    [Test]
    public async Task DrivesVimEndToEnd()
    {
        var vim = Programs.Find("vim") ?? throw new IgnoreException("vim not found on PATH");

        var launched = await CallAsync("terminal_launch", new() { ["command"] = vim, ["args"] = new[] { "-u", "NONE", "-N" } });
        var id = SessionId(launched);
        Assert.That(launched, Does.Contain("alt-screen"), "vim switches to the alternate screen");
        Assert.That(launched, Does.Contain("│~"), "empty-buffer tildes");

        await CallAsync("terminal_press", new() { ["session"] = id, ["keys"] = new[] { "i" } });
        await CallAsync("terminal_type", new() { ["session"] = id, ["text"] = "hello from mcp" });
        var normal = await CallAsync("terminal_press", new() { ["session"] = id, ["keys"] = new[] { "Escape" } });
        Assert.That(normal, Does.Contain(" 0│hello from mcp"));

        // Normal-mode editing: delete the first word.
        var edited = await CallAsync("terminal_press", new() { ["session"] = id, ["keys"] = new[] { "0", "d", "w" } });
        Assert.That(edited, Does.Contain(" 0│from mcp"));

        await CallAsync("terminal_type", new() { ["session"] = id, ["text"] = ":q!\n" });
        var exited = await CallAsync("terminal_wait_for", new() { ["session"] = id, ["until"] = "exit" });
        Assert.That(exited, Does.StartWith("exited with code 0"));
        Assert.That(exited, Does.Not.Contain("alt-screen"), "vim restores the normal screen on exit");
    }
}
