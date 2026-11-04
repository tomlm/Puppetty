using Puppetty.Cli;

namespace Puppetty.Tests;

/// <summary>Where the CLI daemon's Unix domain socket goes, given how long $TMPDIR is.</summary>
public class SocketPathTests
{
    // What macOS gives a user's $TMPDIR, and so .NET's default place for a pipe's socket.
    private const string MacTemp = "/var/folders/n6/hn37q6pj7sj_9dmqqc2113800000gn/T/";

    [Test]
    public void ShortPathsStayWhereDotNetPutsThem()
    {
        Assert.That(Protocol.SocketPath("puppetty-tom", "/tmp/"), Is.EqualTo("puppetty-tom"));
        Assert.That(Protocol.SocketPath("puppetty-runner", MacTemp), Is.EqualTo("puppetty-runner"));
    }

    [Test]
    public void TooLongForTmpdirMovesToTmp()
    {
        // The name the CLI tests use; under macOS's $TMPDIR it came to 117 bytes.
        var name = "puppetty-test-58d3b772d5e343d3b4f8a0d5c4ce5665";
        Assert.That(Protocol.SocketPath(name, MacTemp), Is.EqualTo("/tmp/" + name));
    }

    [Test]
    public void TooLongEvenForTmpIsHashed()
    {
        var name = "puppetty-" + new string('x', 120);
        var path = Protocol.SocketPath(name, MacTemp);
        Assert.That(path, Does.Match("^/tmp/puppetty-[0-9a-f]{16}$"));
        Assert.That(Protocol.SocketPath(name, MacTemp), Is.EqualTo(path), "the same name gives the same path");
        Assert.That(Protocol.SocketPath(name + "y", MacTemp), Is.Not.EqualTo(path), "different names stay apart");
    }
}
