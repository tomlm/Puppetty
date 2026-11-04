namespace Puppetty.Cli;

/// <summary><c>puppetty attach</c>: a live, read-only view of a session in this terminal.</summary>
internal static class Attach
{
    private const string Esc = "\u001b";

    public static async Task<int> RunAsync(string id, CancellationToken cancellationToken)
    {
        await using var pipe = await DaemonClient.ConnectAsync(startIfNeeded: false, cancellationToken);
        if (pipe is null)
        {
            Console.Error.WriteLine("The puppetty daemon is not running.");
            return 1;
        }
        using var reader = Protocol.Reader(pipe);
        await using var writer = Protocol.Writer(pipe);
        await Protocol.WriteAsync(writer, new Request { Op = "attach", Id = id }, cancellationToken);

        var output = Console.Out;
        // Alternate screen, so detaching puts back whatever was in this terminal.
        output.Write($"{Esc}[?1049h{Esc}[H{Esc}[2J");
        Frame? last = null;
        try
        {
            while (await Protocol.ReadAsync<Frame>(reader, cancellationToken) is { } frame)
            {
                last = frame;
                // Each row overwrites the previous frame's row and clears what is left of it.
                var body = frame.Screen.Replace("\r\n", $"{Esc}[K\r\n");
                output.Write($"{Esc}[?25l{Esc}[H{body}{Esc}[K{Esc}[J{Esc}[{frame.CursorRow + 1};{frame.CursorCol + 1}H");
                if (frame.CursorVisible)
                    output.Write($"{Esc}[?25h");
                output.Flush();
                if (frame.Exited)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C: detach.
        }
        finally
        {
            output.Write($"{Esc}[?25h{Esc}[?1049l");
            output.Flush();
        }

        if (last is { Exited: true })
            Console.WriteLine(last.ExitCode is { } code ? $"session {id} exited with code {code}" : last.Screen);
        return 0;
    }
}
