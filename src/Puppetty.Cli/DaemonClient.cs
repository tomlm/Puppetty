using System.Diagnostics;
using System.IO.Pipes;

namespace Puppetty.Cli;

/// <summary>Talks to the daemon, starting it first if it is not running.</summary>
internal static class DaemonClient
{
    public static async Task<Response> SendAsync(Request request, bool startIfNeeded = true, CancellationToken cancellationToken = default)
    {
        await using var pipe = await ConnectAsync(startIfNeeded, cancellationToken);
        if (pipe is null)
            return Response.Failure("The puppetty daemon is not running.");
        using var reader = Protocol.Reader(pipe);
        await using var writer = Protocol.Writer(pipe);
        await Protocol.WriteAsync(writer, request, cancellationToken);
        return await Protocol.ReadAsync<Response>(reader, cancellationToken)
            ?? Response.Failure("The daemon closed the connection without answering.");
    }

    /// <summary>Connects, starting the daemon when allowed. Null when it is not running and may not be started.</summary>
    public static async Task<NamedPipeClientStream?> ConnectAsync(bool startIfNeeded, CancellationToken cancellationToken)
    {
        var pipe = await TryConnectAsync(TimeSpan.FromMilliseconds(200), cancellationToken);
        if (pipe is not null || !startIfNeeded)
            return pipe;

        StartDaemon();
        pipe = await TryConnectAsync(TimeSpan.FromSeconds(15), cancellationToken);
        return pipe ?? throw new IOException($"Started the puppetty daemon but could not connect to pipe '{Protocol.PipeName}'.");
    }

    private static async Task<NamedPipeClientStream?> TryConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", Protocol.PipeName, PipeDirection.InOut, Protocol.Options);
        try
        {
            await pipe.ConnectAsync(timeout, cancellationToken);
            return pipe;
        }
        catch (TimeoutException)
        {
            await pipe.DisposeAsync();
            return null;
        }
    }

    /// <summary>Launches this same program as <c>puppetty daemon</c>, detached from our console.</summary>
    private static void StartDaemon()
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the puppetty executable.");
        var args = new List<string>();
        // Running as `dotnet Puppetty.Cli.dll` rather than the apphost: pass the dll along.
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            args.Add(typeof(DaemonClient).Assembly.Location);
        args.Add("daemon");

        if (OperatingSystem.IsWindows())
        {
            WindowsDetachedProcess.Start(self, args);
            return;
        }

        // On Unix .NET marks its descriptors close-on-exec, so redirecting 0-2 is enough to keep the
        // daemon off the caller's terminal and pipes.
        var startInfo = new ProcessStartInfo(self, args)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the puppetty daemon.");
        process.StandardInput.Close();
    }
}
