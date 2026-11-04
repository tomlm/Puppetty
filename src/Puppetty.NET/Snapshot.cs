using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Puppetty;

/// <summary>
/// Saves and loads <see cref="Screen"/>s as snapshot files: a zip container holding
/// <c>manifest.json</c> (format version, size, cursor, modes, title), <c>cells.bin</c> (the cell
/// grid, binary) and <c>screen.txt</c> (the text, for people; ignored when loading).
/// </summary>
/// <remarks>
/// The container-plus-manifest layout is deliberately close to the <c>.tug</c> format so snapshots
/// can move to it later. Readers reject a newer <c>version</c> rather than misread it.
/// </remarks>
public static class Snapshot
{
    /// <summary>The format version this library writes.</summary>
    public const int Version = 1;

    /// <summary>The conventional file extension.</summary>
    public const string Extension = ".puppetty-snap";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>Writes <paramref name="screen"/> to <paramref name="stream"/>.</summary>
    public static void Save(Screen screen, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var manifest = new Manifest
        {
            Version = Version,
            Cols = screen.Cols,
            Rows = screen.Rows,
            CursorRow = screen.Cursor.Row,
            CursorCol = screen.Cursor.Col,
            CursorVisible = screen.CursorVisible,
            AlternateScreen = screen.IsAlternateScreen,
            Title = screen.Title,
            Modes = screen.Modes,
        };
        Write(zip, "manifest.json", s => JsonSerializer.Serialize(s, manifest, Json));
        Write(zip, "cells.bin", s => WriteCells(screen, s));
        Write(zip, "screen.txt", s => s.Write(Encoding.UTF8.GetBytes(screen.GetText() + "\n")));
    }

    /// <summary>Writes <paramref name="screen"/> to a file, creating its directory.</summary>
    public static void Save(Screen screen, string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } dir)
            Directory.CreateDirectory(dir);
        using var file = File.Create(path);
        Save(screen, file);
    }

    /// <summary>Reads a screen written by <see cref="Save(Screen, Stream)"/>.</summary>
    /// <exception cref="InvalidDataException">Not a snapshot, or a newer format version.</exception>
    public static Screen Load(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Not a Puppetty snapshot: no manifest.json.");
        Manifest manifest;
        using (var s = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<Manifest>(s, Json) ?? throw new InvalidDataException("Empty snapshot manifest.");
        if (manifest.Version > Version)
            throw new InvalidDataException($"Snapshot format version {manifest.Version} is newer than this library reads ({Version}).");

        var cellsEntry = zip.GetEntry("cells.bin") ?? throw new InvalidDataException("Snapshot has no cells.bin.");
        Cell[,] cells;
        using (var s = cellsEntry.Open())
            cells = ReadCells(s, manifest.Rows, manifest.Cols);
        return Screen.FromCells(cells, (manifest.CursorRow, manifest.CursorCol), manifest.CursorVisible, manifest.AlternateScreen,
            manifest.Modes ?? new TerminalModes(false, false, false, MouseTracking.None, "default", false, false, 0), manifest.Title ?? "");
    }

    /// <summary>Reads a snapshot file.</summary>
    public static Screen Load(string path)
    {
        using var file = File.OpenRead(path);
        return Load(file);
    }

    // cells.bin: row-major, one record per cell:
    //   text (7-bit-length-prefixed UTF-8), width (byte), foreground, background, attributes (uint16)
    // where a colour is kind (byte: 0 default, 1 palette, 2 rgb) + value (int32).
    private static void WriteCells(Screen screen, Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        for (var r = 0; r < screen.Rows; r++)
        {
            for (var c = 0; c < screen.Cols; c++)
            {
                var cell = screen[r, c];
                w.Write(cell.Text);
                w.Write((byte)cell.Width);
                WriteColor(w, cell.Foreground);
                WriteColor(w, cell.Background);
                w.Write((ushort)cell.Attributes);
            }
        }
    }

    private static Cell[,] ReadCells(Stream stream, int rows, int cols)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var cells = new Cell[rows, cols];
        for (var row = 0; row < rows; row++)
            for (var col = 0; col < cols; col++)
                cells[row, col] = new Cell(r.ReadString(), r.ReadByte(), ReadColor(r), ReadColor(r), (CellAttributes)r.ReadUInt16());
        return cells;
    }

    private static void WriteColor(BinaryWriter w, Color color)
    {
        w.Write((byte)color.Kind);
        w.Write(color.Value);
    }

    private static Color ReadColor(BinaryReader r)
    {
        var kind = (ColorKind)r.ReadByte();
        var value = r.ReadInt32();
        return kind switch
        {
            ColorKind.Default => Color.Default,
            ColorKind.Palette => Color.Palette(value),
            ColorKind.Rgb => Color.Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value),
            _ => throw new InvalidDataException($"Unknown colour kind {(byte)kind} in snapshot."),
        };
    }

    private static void Write(ZipArchive zip, string name, Action<Stream> write)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        write(s);
    }

    private sealed class Manifest
    {
        public int Version { get; set; }
        public int Cols { get; set; }
        public int Rows { get; set; }
        public int CursorRow { get; set; }
        public int CursorCol { get; set; }
        public bool CursorVisible { get; set; }
        public bool AlternateScreen { get; set; }
        public string? Title { get; set; }
        public TerminalModes? Modes { get; set; }
    }
}
