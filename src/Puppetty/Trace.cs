using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Emulator = XTerm.Terminal;
using EmulatorOptions = XTerm.Options.TerminalOptions;

namespace Puppetty;

/// <summary>What a <see cref="TraceEvent"/> records.</summary>
public enum TraceEventKind : byte
{
    /// <summary>Bytes the program wrote to the terminal.</summary>
    Output = 1,
    /// <summary>Bytes sent to the program (keys, paste, mouse, and replies to its queries).</summary>
    Input = 2,
    /// <summary>The terminal was resized.</summary>
    Resize = 3,
    /// <summary>An action finished waiting for the screen to settle; <see cref="TraceEvent.Note"/> says how.</summary>
    Settled = 4,
}

/// <summary>One timestamped entry in a <see cref="Trace"/>.</summary>
/// <param name="Time">Time since launch.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Data">The bytes, for output and input.</param>
/// <param name="Cols">New width, for a resize.</param>
/// <param name="Rows">New height, for a resize.</param>
/// <param name="Note">How the screen settled, for a settle.</param>
public sealed record TraceEvent(TimeSpan Time, TraceEventKind Kind, byte[] Data, int Cols = 0, int Rows = 0, string? Note = null);

/// <summary>
/// Everything that passed through a terminal session: the program's raw output, the input sent to
/// it, resizes and settle decisions, each timestamped. <see cref="Replay"/> feeds the output back
/// through the emulator to rebuild the screen at any moment.
/// </summary>
/// <remarks>
/// Saved as a zip container with <c>manifest.json</c> and a binary <c>events.bin</c>.
/// Recording stops (and <see cref="Truncated"/> is set) once <see cref="LaunchOptions.MaxTraceBytes"/> is reached.
/// </remarks>
public sealed class Trace
{
    /// <summary>The format version this library writes.</summary>
    public const int Version = 1;

    /// <summary>The conventional file extension.</summary>
    public const string Extension = ".puppetty-trace";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal Trace(string command, IReadOnlyList<string> args, int cols, int rows, int scrollback, DateTimeOffset startedAt, SettleOptions settle, bool truncated, IReadOnlyList<TraceEvent> events)
    {
        Command = command;
        Args = args;
        Cols = cols;
        Rows = rows;
        Scrollback = scrollback;
        StartedAt = startedAt;
        Settle = settle;
        Truncated = truncated;
        Events = events;
    }

    /// <summary>The program that was launched.</summary>
    public string Command { get; }

    /// <summary>Its arguments.</summary>
    public IReadOnlyList<string> Args { get; }

    /// <summary>Width at launch.</summary>
    public int Cols { get; }

    /// <summary>Height at launch.</summary>
    public int Rows { get; }

    /// <summary>Scrollback lines the emulator kept.</summary>
    public int Scrollback { get; }

    /// <summary>When the program was launched.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>The settle timings in effect.</summary>
    public SettleOptions Settle { get; }

    /// <summary>True when recording stopped early because the size limit was reached.</summary>
    public bool Truncated { get; }

    /// <summary>The events, in order.</summary>
    public IReadOnlyList<TraceEvent> Events { get; }

    /// <summary>Rebuilds the screen by replaying output and resizes up to <paramref name="until"/> (default: the end).</summary>
    public Screen Replay(TimeSpan? until = null)
    {
        var emulator = new Emulator(new EmulatorOptions { Cols = Cols, Rows = Rows, Scrollback = Scrollback });
        foreach (var e in Events)
        {
            if (until is { } limit && e.Time > limit)
                break;
            if (e.Kind == TraceEventKind.Output)
                emulator.Write(e.Data);
            else if (e.Kind == TraceEventKind.Resize)
                emulator.Resize(e.Cols, e.Rows);
        }
        return Screen.Capture(emulator);
    }

    /// <summary>A readable timeline: one line per event, with input and output shown as escaped text.</summary>
    public string ToTimeline(int maxBytesPerEvent = 120)
    {
        var sb = new StringBuilder();
        sb.Append($"{Command} {string.Join(' ', Args)}  {Cols}x{Rows}  started {StartedAt:O}{(Truncated ? "  (truncated)" : "")}\n");
        foreach (var e in Events)
        {
            sb.Append($"{e.Time.TotalSeconds,9:F3}s  ");
            sb.Append(e.Kind switch
            {
                TraceEventKind.Output => "out  " + Escape(e.Data, maxBytesPerEvent),
                TraceEventKind.Input => "in   " + Escape(e.Data, maxBytesPerEvent),
                TraceEventKind.Resize => $"resize {e.Cols}x{e.Rows}",
                TraceEventKind.Settled => "settled " + e.Note,
                _ => e.Kind.ToString(),
            });
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string Escape(byte[] data, int max)
    {
        var text = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, max));
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                '\u001b' => "\\e",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                < ' ' or '\u007f' => $"\\x{(int)ch:x2}",
                _ => ch.ToString(),
            });
        }
        if (data.Length > max)
            sb.Append($"… (+{data.Length - max} bytes)");
        return sb.ToString();
    }

    /// <summary>Writes the trace to <paramref name="stream"/>.</summary>
    public void Save(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        using (var s = zip.CreateEntry("manifest.json").Open())
        {
            JsonSerializer.Serialize(s, new Manifest
            {
                Version = Version,
                Command = Command,
                Args = [.. Args],
                Cols = Cols,
                Rows = Rows,
                Scrollback = Scrollback,
                StartedAt = StartedAt,
                QuietPeriodMs = Settle.QuietPeriod.TotalMilliseconds,
                EscapeQuietPeriodMs = Settle.EscapeQuietPeriod.TotalMilliseconds,
                MaxWaitMs = Settle.MaxWait.TotalMilliseconds,
                Truncated = Truncated,
            }, Json);
        }
        // events.bin: per event, kind (byte) + time in ticks (int64), then
        //   output/input: length (int32) + bytes;  resize: cols, rows (int32);  settled: note (string)
        using (var s = zip.CreateEntry("events.bin").Open())
        using (var w = new BinaryWriter(s, Encoding.UTF8))
        {
            foreach (var e in Events)
            {
                w.Write((byte)e.Kind);
                w.Write(e.Time.Ticks);
                switch (e.Kind)
                {
                    case TraceEventKind.Output or TraceEventKind.Input:
                        w.Write(e.Data.Length);
                        w.Write(e.Data);
                        break;
                    case TraceEventKind.Resize:
                        w.Write(e.Cols);
                        w.Write(e.Rows);
                        break;
                    case TraceEventKind.Settled:
                        w.Write(e.Note ?? "");
                        break;
                }
            }
        }
        using (var s = zip.CreateEntry("timeline.txt").Open())
            s.Write(Encoding.UTF8.GetBytes(ToTimeline()));
    }

    /// <summary>Writes the trace to a file, creating its directory.</summary>
    public void Save(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } dir)
            Directory.CreateDirectory(dir);
        using var file = File.Create(path);
        Save(file);
    }

    /// <summary>Reads a trace written by <see cref="Save(Stream)"/>.</summary>
    /// <exception cref="InvalidDataException">Not a trace, or a newer format version.</exception>
    public static Trace Load(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        Manifest manifest;
        using (var s = (zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Not a Puppetty trace: no manifest.json.")).Open())
            manifest = JsonSerializer.Deserialize<Manifest>(s, Json) ?? throw new InvalidDataException("Empty trace manifest.");
        if (manifest.Version > Version)
            throw new InvalidDataException($"Trace format version {manifest.Version} is newer than this library reads ({Version}).");

        var events = new List<TraceEvent>();
        using (var s = (zip.GetEntry("events.bin") ?? throw new InvalidDataException("Trace has no events.bin.")).Open())
        using (var r = new BinaryReader(s, Encoding.UTF8))
        {
            while (true)
            {
                int kindByte;
                try { kindByte = r.ReadByte(); }
                catch (EndOfStreamException) { break; }
                var kind = (TraceEventKind)kindByte;
                var time = TimeSpan.FromTicks(r.ReadInt64());
                events.Add(kind switch
                {
                    TraceEventKind.Output or TraceEventKind.Input => new TraceEvent(time, kind, r.ReadBytes(r.ReadInt32())),
                    TraceEventKind.Resize => new TraceEvent(time, kind, [], r.ReadInt32(), r.ReadInt32()),
                    TraceEventKind.Settled => new TraceEvent(time, kind, [], Note: r.ReadString()),
                    _ => throw new InvalidDataException($"Unknown trace event kind {kindByte}."),
                });
            }
        }
        var settle = new SettleOptions
        {
            QuietPeriod = TimeSpan.FromMilliseconds(manifest.QuietPeriodMs),
            EscapeQuietPeriod = TimeSpan.FromMilliseconds(manifest.EscapeQuietPeriodMs),
            MaxWait = TimeSpan.FromMilliseconds(manifest.MaxWaitMs),
        };
        return new Trace(manifest.Command ?? "", manifest.Args ?? [], manifest.Cols, manifest.Rows, manifest.Scrollback, manifest.StartedAt, settle, manifest.Truncated, events);
    }

    /// <summary>Reads a trace file.</summary>
    public static Trace Load(string path)
    {
        using var file = File.OpenRead(path);
        return Load(file);
    }

    private sealed class Manifest
    {
        public int Version { get; set; }
        public string? Command { get; set; }
        public string[]? Args { get; set; }
        public int Cols { get; set; }
        public int Rows { get; set; }
        public int Scrollback { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public double QuietPeriodMs { get; set; }
        public double EscapeQuietPeriodMs { get; set; }
        public double MaxWaitMs { get; set; }
        public bool Truncated { get; set; }
    }
}

/// <summary>Collects trace events from a running terminal. Thread-safe.</summary>
internal sealed class TraceRecorder(long maxBytes, DateTimeOffset start)
{
    private readonly List<TraceEvent> _events = [];
    private long _bytes;
    private bool _truncated;

    public void Record(DateTimeOffset now, TraceEventKind kind, ReadOnlySpan<byte> data = default, int cols = 0, int rows = 0, string? note = null)
    {
        lock (_events)
        {
            if (_truncated)
                return;
            if (_bytes + data.Length > maxBytes)
            {
                _truncated = true;
                return;
            }
            _bytes += data.Length;
            _events.Add(new TraceEvent(now - start, kind, data.ToArray(), cols, rows, note));
        }
    }

    public (IReadOnlyList<TraceEvent> Events, bool Truncated) Snapshot()
    {
        lock (_events)
            return (_events.ToArray(), _truncated);
    }
}
