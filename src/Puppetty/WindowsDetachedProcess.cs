using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Puppetty.Cli;

/// <summary>
/// Starts a process that inherits no handles and has no console.
/// </summary>
/// <remarks>
/// Process.Start always passes bInheritHandles=TRUE, so the daemon would inherit the caller's
/// stdout. Whoever reads that output (a shell capturing <c>$(puppetty start ...)</c>, a test
/// harness) then never sees end-of-file until the daemon exits, and hangs.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsDetachedProcess
{
    private const uint DetachedProcess = 0x00000008;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    public static void Start(string program, IEnumerable<string> args)
    {
        var commandLine = new StringBuilder(Quote(program));
        foreach (var arg in args)
            commandLine.Append(' ').Append(Quote(arg));

        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcessW(null, commandLine, 0, 0, bInheritHandles: false,
                DetachedProcess | CreateNewProcessGroup | CreateUnicodeEnvironment, 0, null, ref startup, out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the puppetty daemon.");
        CloseHandle(info.hProcess);
        CloseHandle(info.hThread);
    }

    // Paths and our own arguments contain no quotes, so wrapping is enough.
    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? lpApplicationName, StringBuilder lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, nint lpEnvironment, string? lpCurrentDirectory, ref StartupInfo lpStartupInfo, out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
