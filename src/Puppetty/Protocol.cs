using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Puppetty.Cli;

/// <summary>
/// One request per connection: the client writes a <see cref="Request"/> as a JSON line and reads
/// back a <see cref="Response"/> line (or, for <c>attach</c>, a stream of <see cref="Frame"/> lines).
/// </summary>
internal sealed class Request
{
    public string Op { get; set; } = "";
    public string? Id { get; set; }
    public string? Command { get; set; }
    public string[]? Args { get; set; }
    public int? Cols { get; set; }
    public int? Rows { get; set; }
    public string? Cwd { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public string? Text { get; set; }
    public string[]? Keys { get; set; }
    public string? Regex { get; set; }
    public string? Until { get; set; }
    public double? TimeoutSeconds { get; set; }
    public string? Format { get; set; }
    public int? Row { get; set; }
    public int? Col { get; set; }
    public string? Button { get; set; }
    public int? Lines { get; set; }
}

internal sealed class Response
{
    public bool Ok { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }

    public static Response Success(string output = "") => new() { Ok = true, Output = output };
    public static Response Failure(string error) => new() { Ok = false, Error = error };
}

/// <summary>One screen update streamed to <c>puppetty attach</c>.</summary>
internal sealed class Frame
{
    public string Screen { get; set; } = "";
    public int CursorRow { get; set; }
    public int CursorCol { get; set; }
    public bool CursorVisible { get; set; }
    public bool Exited { get; set; }
    public int? ExitCode { get; set; }
}

internal static class Protocol
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The daemon's pipe: a named pipe on Windows, a Unix domain socket elsewhere (.NET maps the name).
    /// Per user; <c>PUPPETTY_PIPE</c> overrides it (tests use this to get a private daemon).
    /// </summary>
    public static string PipeName =>
        Environment.GetEnvironmentVariable("PUPPETTY_PIPE") is { Length: > 0 } name ? name : $"puppetty-{Environment.UserName}";

    // CurrentUserOnly: other users on the machine cannot connect and drive your sessions.
    public const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    public static StreamWriter Writer(Stream stream) => new(stream, Utf8, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

    public static StreamReader Reader(Stream stream) => new(stream, Utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

    public static Task WriteAsync<T>(StreamWriter writer, T message, CancellationToken cancellationToken = default) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(message, Json).AsMemory(), cancellationToken);

    public static async Task<T?> ReadAsync<T>(StreamReader reader, CancellationToken cancellationToken = default) where T : class
    {
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return line is null ? null : JsonSerializer.Deserialize<T>(line, Json);
    }
}
