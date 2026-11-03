using Emulator = XTerm.Terminal;

namespace Puppetty.Tests;

public class KeyEncoderTests
{
    private static string Encode(string chord, Emulator emulator, bool conPty = false) =>
        KeyEncoder.Encode(KeyChord.Parse(chord), emulator, conPty);

    [TestCase("Enter", "\r")]
    [TestCase("Tab", "\t")]
    [TestCase("Escape", "\u001b")]
    [TestCase("Up", "\u001b[A")]
    [TestCase("Left", "\u001b[D")]
    [TestCase("Ctrl+C", "\u0003")]
    [TestCase("Ctrl+c", "\u0003")]
    [TestCase("Alt+f", "\u001bf")]
    [TestCase("a", "a")]
    [TestCase("Space", " ")]
    [TestCase("F1", "\u001bOP")]
    public void EncodesNormalModeVt(string chord, string expected)
    {
        Assert.That(Encode(chord, new Emulator()), Is.EqualTo(expected));
    }

    [Test]
    public void ArrowsFollowApplicationCursorKeysMode()
    {
        var emulator = new Emulator();
        emulator.Write("\u001b[?1h"); // DECCKM on
        Assert.That(Encode("Up", emulator), Is.EqualTo("\u001bOA"));

        emulator.Write("\u001b[?1l"); // and off again
        Assert.That(Encode("Up", emulator), Is.EqualTo("\u001b[A"));
    }

    [Test]
    public void ConPtyWithoutWin32InputModeSendsVtExceptBareEscape()
    {
        var emulator = new Emulator();
        Assert.That(Encode("Up", emulator, conPty: true), Is.EqualTo("\u001b[A"));
        // Bare Escape goes as a win32 record: VK_ESCAPE (27), scan 1, char 27, down then up.
        Assert.That(Encode("Escape", emulator, conPty: true), Is.EqualTo("\u001b[27;1;27;1;0;1_\u001b[27;1;27;0;0;1_"));
    }

    [Test]
    public void ConPtyWin32InputModeSendsInputRecords()
    {
        var emulator = new Emulator();
        emulator.Write("\u001b[?9001h");
        Assert.That(emulator.Win32InputMode, Is.True);

        // VK_RETURN (13), char CR.
        Assert.That(Encode("Enter", emulator, conPty: true), Does.StartWith("\u001b[13;").And.Contain(";13;1;0;1_"));
        // VK_UP (38), no char, enhanced key flag (0x100).
        Assert.That(Encode("Up", emulator, conPty: true), Does.StartWith("\u001b[38;").And.Contain(";0;1;256;1_"));
        // Ctrl+C: VK_C (67), char 3, left ctrl (0x8).
        Assert.That(Encode("Ctrl+c", emulator, conPty: true), Does.StartWith("\u001b[67;").And.Contain(";3;1;8;1_"));
    }

    [Test]
    public void Win32InputModeIsIgnoredOffConPty()
    {
        var emulator = new Emulator();
        emulator.Write("\u001b[?9001h");
        Assert.That(Encode("Up", emulator, conPty: false), Is.EqualTo("\u001b[A"));
    }
}
