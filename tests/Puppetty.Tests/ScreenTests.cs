using System.Text.RegularExpressions;
using Emulator = XTerm.Terminal;
using EmulatorOptions = XTerm.Options.TerminalOptions;

namespace Puppetty.Tests;

/// <summary>Screen capture and locator resolution against an emulator fed directly, no PTY.</summary>
public class ScreenTests
{
    private static Screen Render(string vt, int cols = 20, int rows = 5)
    {
        var emulator = new Emulator(new EmulatorOptions { Cols = cols, Rows = rows });
        emulator.Write(vt);
        return Screen.Capture(emulator);
    }

    [Test]
    public void CapturesColoursAndAttributes()
    {
        var s = Render("\u001b[31mR\u001b[0m\u001b[1;4mB\u001b[0m\u001b[7mI\u001b[0m\u001b[38;2;255;128;0mT\u001b[0m\u001b[48;5;200mG\u001b[0m\u001b[94mX");

        Assert.That(s[0, 0], Is.EqualTo(new Cell("R", 1, AnsiColor.Red, Color.Default, CellAttributes.None)));
        Assert.That(s[0, 1].Has(CellAttributes.Bold | CellAttributes.Underline), Is.True);
        Assert.That(s[0, 2].Attributes, Is.EqualTo(CellAttributes.Inverse));
        Assert.That(s[0, 3].Foreground, Is.EqualTo(Color.Rgb(255, 128, 0)));
        Assert.That(s[0, 4].Background, Is.EqualTo(Color.Palette(200)));
        Assert.That(s[0, 5].Foreground, Is.EqualTo((Color)AnsiColor.BrightBlue));
        Assert.That(s[0, 6], Is.EqualTo(new Cell(" ", 1, Color.Default, Color.Default, CellAttributes.None)));
    }

    [Test]
    public void WideCharactersOccupyTwoColumns()
    {
        var s = Render("日本 ok");
        Assert.That(s[0, 0].Text, Is.EqualTo("日"));
        Assert.That(s[0, 0].Width, Is.EqualTo(2));
        Assert.That(s[0, 1].Width, Is.EqualTo(0));
        Assert.That(s[0, 1].Text, Is.EqualTo(""));
        Assert.That(s.Line(0), Is.EqualTo("日本 ok"));

        // "ok" is at column 5 although it is at string index 3.
        var match = new TextLocator(null!, "ok").Resolve(s).Single();
        Assert.That((match.Row, match.Col), Is.EqualTo((0, 5)));
    }

    [Test]
    public void TextLocatorFindsEveryOccurrenceInOrder()
    {
        var s = Render("ab ab\r\nxx ab");
        var matches = new TextLocator(null!, "ab").Resolve(s);
        Assert.That(matches.Select(m => (m.Row, m.Col)), Is.EqualTo(new[] { (0, 0), (0, 3), (1, 3) }));
    }

    [Test]
    public void RegexLocatorReturnsMatchedText()
    {
        var s = Render("total 42 files\r\ntotal 7");
        var matches = new TextLocator(null!, new Regex(@"total \d+")).Resolve(s);
        Assert.That(matches.Select(m => m.Text), Is.EqualTo(new[] { "total 42", "total 7" }));
    }

    [Test]
    public void AnnotatedFormatShowsModesSpansAndNumberedRows()
    {
        var s = Render("\u001b]2;demo\u0007\u001b[?1000h\u001b[?1006h\u001b[?2004hhello\r\n\u001b[7m sel \u001b[0m\r\n\u001b[31mERR\u001b[0m", cols: 20, rows: 12);
        var text = s.Render(ScreenFormat.Annotated);

        Assert.That(text, Is.EqualTo(
            "size 20x12 · cursor 2,3 · mouse pressrelease/sgr · bracketed-paste · title \"demo\"\n" +
            "styles: r1 0-4 inverse; r2 0-2 fg=red\n" +
            " 0│hello\n" +
            " 1│ sel\n" +
            " 2│ERR\n" +
            "(rows 3-11 blank)\n"));
    }

    [Test]
    public void AnsiFormatRoundTripsThroughTheEmulator()
    {
        var original = Render("plain \u001b[1;31mbold red\u001b[0m \u001b[48;2;1;2;3mrgb\u001b[0m\r\n\u001b[7m   \u001b[0m");
        var replayed = Render(original.Render(ScreenFormat.Ansi));

        for (var r = 0; r < original.Rows; r++)
            for (var c = 0; c < original.Cols; c++)
                Assert.That(replayed[r, c], Is.EqualTo(original[r, c]), $"cell ({r}, {c})");
    }

    [Test]
    public void RegionTextIsClippedAndTrimmed()
    {
        var s = Render("0123456789\r\nabcdefghij\r\nKLMNOPQRST");
        Assert.That(s.GetText(1, 2, 3, 2), Is.EqualTo("cde\nMNO"));
        Assert.That(s.GetText(2, 8, 100, 100), Is.EqualTo("ST\n\n"), "clipped to the 5-row screen");

        var region = new RegionLocator(null!, 0, 15, 5, 1);
        Assert.That(region.Resolve(s), Is.Empty, "a blank region is not visible");
    }
}
