using Porta.Pty;

namespace Puppetty.Tests;

[Platform("Win")]
public class ConPtyTests
{
    // The built-in conhost swallows Kitty graphics, XTGETTCAP, DECRQSS and OSC 10/11/52, and the
    // fallback to it is silent. See docs/conpty-quirks.md.
    [Test]
    public void UsesBundledOpenConsole()
    {
        Assert.That(PtyProvider.PseudoConsoleImplementation, Is.EqualTo("oob"));
    }
}
