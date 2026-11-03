namespace Puppetty.Tests;

/// <summary>Launches modes of the Puppetty.Fixture app (copied into the test output).</summary>
internal static class Fixture
{
    private static readonly string Dll = Path.Combine(AppContext.BaseDirectory, "Puppetty.Fixture.dll");

    // `dotnet test` sets DOTNET_HOST_PATH to the host it is running under.
    private static readonly string DotNet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    public static Task<Terminal> LaunchAsync(params string[] args) =>
        Terminal.LaunchAsync(DotNet, new LaunchOptions { Args = [Dll, .. args] });
}
