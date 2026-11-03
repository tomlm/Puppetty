namespace Puppetty.Tests;

public class KeyChordTests
{
    [TestCase("Enter", Key.Enter, KeyModifiers.None)]
    [TestCase("return", Key.Enter, KeyModifiers.None)]
    [TestCase("Esc", Key.Escape, KeyModifiers.None)]
    [TestCase("Up", Key.Up, KeyModifiers.None)]
    [TestCase("ArrowDown", Key.Down, KeyModifiers.None)]
    [TestCase("F5", Key.F5, KeyModifiers.None)]
    [TestCase("Shift+Tab", Key.Tab, KeyModifiers.Shift)]
    [TestCase("ctrl+alt+Delete", Key.Delete, KeyModifiers.Control | KeyModifiers.Alt)]
    [TestCase("Space", Key.Space, KeyModifiers.None)]
    public void ParsesNamedKeys(string text, Key key, KeyModifiers modifiers)
    {
        var chord = KeyChord.Parse(text);
        Assert.That(chord.Key, Is.EqualTo(key));
        Assert.That(chord.Character, Is.Null);
        Assert.That(chord.Modifiers, Is.EqualTo(modifiers));
    }

    [TestCase("a", 'a', KeyModifiers.None)]
    [TestCase("Ctrl+C", 'C', KeyModifiers.Control)]
    [TestCase("Alt+f", 'f', KeyModifiers.Alt)]
    [TestCase("+", '+', KeyModifiers.None)]
    [TestCase("Ctrl++", '+', KeyModifiers.Control)]
    [TestCase("1", '1', KeyModifiers.None)]
    public void ParsesCharacterKeys(string text, char character, KeyModifiers modifiers)
    {
        var chord = KeyChord.Parse(text);
        Assert.That(chord.Key, Is.Null);
        Assert.That(chord.Character, Is.EqualTo(character));
        Assert.That(chord.Modifiers, Is.EqualTo(modifiers));
    }

    [TestCase("")]
    [TestCase("Hyper+A")]
    [TestCase("NotAKey")]
    [TestCase("12")]
    public void RejectsUnknownChords(string text)
    {
        Assert.Throws<FormatException>(() => KeyChord.Parse(text));
    }
}
