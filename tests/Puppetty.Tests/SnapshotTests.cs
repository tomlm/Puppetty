using Emulator = XTerm.Terminal;
using EmulatorOptions = XTerm.Options.TerminalOptions;

namespace Puppetty.Tests;

public class SnapshotTests
{
    private static Screen Render(string vt, int cols = 20, int rows = 4)
    {
        var emulator = new Emulator(new EmulatorOptions { Cols = cols, Rows = rows });
        emulator.Write(vt);
        return Screen.Capture(emulator);
    }

    [Test]
    public void RoundTripsCellsCursorModesAndTitle()
    {
        var original = Render("\u001b]2;t\u0007\u001b[?2004h\u001b[1;31mred\u001b[0m 日本 \u001b[38;2;9;8;7;48;5;200mx\r\n\u001b[7msel\u001b[?25l");
        using var stream = new MemoryStream();
        Snapshot.Save(original, stream);
        stream.Position = 0;
        var loaded = Snapshot.Load(stream);

        Assert.That(ScreenDiff.Compare(original, loaded, new ScreenDiffOptions { CompareCursor = true }).IsMatch, Is.True);
        Assert.That(loaded.GetText(), Is.EqualTo(original.GetText()));
        Assert.That(loaded.Cursor, Is.EqualTo(original.Cursor));
        Assert.That(loaded.CursorVisible, Is.False);
        Assert.That(loaded.Title, Is.EqualTo("t"));
        Assert.That(loaded.Modes, Is.EqualTo(original.Modes));
        Assert.That(loaded[0, 4].Width, Is.EqualTo(2));
    }

    [Test]
    public void RejectsNewerFormatVersions()
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            w.Write("{\"version\": 99, \"cols\": 1, \"rows\": 1}");
        stream.Position = 0;
        Assert.That(() => Snapshot.Load(stream), Throws.TypeOf<InvalidDataException>().With.Message.Contains("version 99"));
    }

    [Test]
    public void DiffMarksTextAndStyleChangesSideBySide()
    {
        var expected = Render("hello world\r\nsame\r\nplain");
        var actual = Render("hello wurld\r\nsame\r\n\u001b[1mplain");

        var diff = ScreenDiff.Compare(expected, actual);
        Assert.That(diff.IsMatch, Is.False);
        Assert.That(diff.Cells, Has.Count.EqualTo(6));
        Assert.That(diff.Cells.Count(d => d.StyleOnly), Is.EqualTo(5));

        var text = diff.ToString();
        Assert.That(text, Does.Contain("6 cell(s) differ (5 in style only)"));
        Assert.That(text, Does.Contain("hello world          0 │ hello wurld\n"));
        Assert.That(text, Does.Contain("                       │        ^\n"));
        Assert.That(text, Does.Contain("                       │ ~~~~~\n"));

        Assert.That(ScreenDiff.Compare(expected, actual, new ScreenDiffOptions { CompareStyles = false }).Cells, Has.Count.EqualTo(1));
    }

    [Test]
    public void DiffReportsSizeMismatch()
    {
        var diff = ScreenDiff.Compare(Render("a", cols: 10), Render("a", cols: 12));
        Assert.That(diff.IsMatch, Is.False);
        Assert.That(diff.ToString(), Does.StartWith("size differs: expected 10x4, actual 12x4"));
    }
}
