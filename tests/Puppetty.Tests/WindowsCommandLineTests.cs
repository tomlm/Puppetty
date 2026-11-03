namespace Puppetty.Tests;

public class WindowsCommandLineTests
{
    [TestCase("plain", "plain")]
    [TestCase("", "\"\"")]
    [TestCase("has space", "\"has space\"")]
    [TestCase("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [TestCase(@"C:\dir\", @"C:\dir\")]
    [TestCase(@"C:\my dir\", @"""C:\my dir\\""")]
    [TestCase(@"a\\""b", @"""a\\\\\""b""")]
    [TestCase(@"no\quote", @"no\quote")]
    public void QuotesLikeMsvcrt(string arg, string expected)
    {
        Assert.That(WindowsCommandLine.Quote(arg), Is.EqualTo(expected));
    }
}
