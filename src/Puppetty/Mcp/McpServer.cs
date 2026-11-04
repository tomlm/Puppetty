using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Puppetty.Mcp;

/// <summary><c>puppetty mcp</c>: the MCP server, over stdin/stdout.</summary>
internal static class McpServer
{
    public static async Task RunAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        // stdout carries the MCP protocol; everything else goes to stderr.
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton<SessionManager>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<TerminalTools>();

        var app = builder.Build();
        await app.RunAsync();

        // Kill every program the agent left running.
        await app.Services.GetRequiredService<SessionManager>().DisposeAsync();
    }
}
