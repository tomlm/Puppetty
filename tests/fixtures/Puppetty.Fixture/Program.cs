// Tiny programs for Puppetty's end-to-end tests, one per mode:
//
//   echo        "> " prompt; echoes each line back as "echo: <line>"; "quit" exits
//   menu        arrow-key menu; Enter prints "Selected: <item>" and exits with the item's index
//   exit <n>    prints "exiting with <n>" and exits with code n
//   styles      prints styled and wide text, then waits for a key and exits
//   delayed <ms> <text>
//               prints "waiting", then <text> after <ms> milliseconds, then waits for a key
//   sync        on each key, draws a frame inside a synchronized-output block (DEC 2026) with a
//               pause in the middle; "q" exits
//   raw [seq...]
//               writes each seq as CSI <seq> (e.g. "?1000h"), switches the terminal to raw input,
//               then prints every input chunk as "got: <text>" with ESC shown as \e; "q" exits
//   size        prints "size <cols>x<rows>" at start and after each key; "q" exits

using System.Runtime.InteropServices;
using System.Text;

const string Esc = "\u001b";

return args.FirstOrDefault() switch
{
    "echo" => Echo(),
    "menu" => Menu(),
    "exit" => Exit(int.Parse(args[1])),
    "styles" => Styles(),
    "delayed" => Delayed(int.Parse(args[1]), args[2]),
    "sync" => Sync(),
    "raw" => Raw(args[1..]),
    "size" => Size(),
    _ => Usage(),
};

static int Echo()
{
    while (true)
    {
        Console.Write("> ");
        var line = Console.ReadLine();
        if (line is null || line == "quit")
            return 0;
        Console.WriteLine($"echo: {line}");
    }
}

static int Menu()
{
    string[] items = ["Apple", "Banana", "Cherry"];
    var selected = 0;
    while (true)
    {
        // Home + erase display, then redraw everything: the screen is the whole state.
        Console.Write($"{Esc}[H{Esc}[2J");
        Console.WriteLine("Pick a fruit:");
        for (var i = 0; i < items.Length; i++)
            Console.WriteLine($"{(i == selected ? ">" : " ")} {items[i]}");

        var key = Console.ReadKey(intercept: true);
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                selected = (selected + items.Length - 1) % items.Length;
                break;
            case ConsoleKey.DownArrow:
                selected = (selected + 1) % items.Length;
                break;
            case ConsoleKey.Enter:
                Console.WriteLine($"Selected: {items[selected]}");
                return selected;
        }
    }
}

static int Exit(int code)
{
    Console.WriteLine($"exiting with {code}");
    return code;
}

static int Styles()
{
    Console.WriteLine($"{Esc}[31mERROR{Esc}[0m plain");                                 // row 0: red, then default
    Console.WriteLine($"{Esc}[1;4mBold{Esc}[0m {Esc}[7mSel{Esc}[0m");                   // row 1: bold+underline, reverse
    Console.WriteLine($"{Esc}[38;2;255;128;0mRGB{Esc}[0m {Esc}[48;5;200mBG{Esc}[0m");   // row 2: truecolor fg, 256 bg
    Console.WriteLine("日本 ok");                                                         // row 3: wide characters
    Console.ReadKey(intercept: true);
    return 0;
}

static int Delayed(int milliseconds, string text)
{
    Console.WriteLine("waiting");
    Thread.Sleep(milliseconds);
    Console.WriteLine(text);
    Console.ReadKey(intercept: true);
    return 0;
}

static int Sync()
{
    var frame = 0;
    Console.WriteLine("ready");
    while (Console.ReadKey(intercept: true).KeyChar != 'q')
    {
        frame++;
        // Without honouring 2026, a 75 ms quiet period would see the half-drawn frame.
        Console.Write($"{Esc}[?2026h{Esc}[H{Esc}[2J");
        Console.Write($"frame {frame}: drawing");
        Console.Out.Flush();
        Thread.Sleep(500);
        Console.Write($"\r{Esc}[Kframe {frame}: done");
        Console.Write($"{Esc}[?2026l");
        Console.Out.Flush();
    }
    return 0;
}

static int Raw(string[] sequences)
{
    foreach (var seq in sequences)
        Console.Write($"{Esc}[{seq}");
    Console.Out.Flush();
    // After the first write: on Unix, .NET sets up the terminal when the console is first used,
    // which would undo raw mode set before it.
    RawInput.Enable();
    // Explicit CR: raw mode turns off the LF -> CRLF translation.
    Console.Write("raw ready\r\n");
    Console.Out.Flush();

    var buffer = new byte[4096];
    while (true)
    {
        var read = RawInput.Read(buffer);
        if (read <= 0)
            return 0;
        var text = Encoding.UTF8.GetString(buffer, 0, read);
        if (text == "q")
            return 0;
        var shown = new StringBuilder();
        foreach (var ch in text)
            shown.Append(ch switch { '\u001b' => "\\e", '\r' => "\\r", '\n' => "\\n", < ' ' => $"^{(char)(ch + '@')}", _ => ch.ToString() });
        Console.Write($"got: {shown}\r\n");
        Console.Out.Flush();
    }
}

static int Size()
{
    // Prints the window size it sees on start and after each key; "q" exits.
    do
        Console.WriteLine($"size {Console.WindowWidth}x{Console.WindowHeight}");
    while (Console.ReadKey(intercept: true).KeyChar != 'q');
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("usage: Puppetty.Fixture echo | menu | exit <n> | styles | delayed <ms> <text> | sync | raw [seq...]");
    return 2;
}

/// <summary>Puts the terminal into raw input: no line buffering, no echo, escape sequences delivered as bytes.</summary>
static class RawInput
{
    public static void Enable()
    {
        if (OperatingSystem.IsWindows())
        {
            const uint ProcessedInput = 0x1, LineInput = 0x2, EchoInput = 0x4, VirtualTerminalInput = 0x200;
            var handle = GetStdHandle(-10);
            GetConsoleMode(handle, out var mode);
            SetConsoleMode(handle, (mode & ~(ProcessedInput | LineInput | EchoInput)) | VirtualTerminalInput);
        }
        else
        {
            // Not `stty raw`: .NET puts its own terminal settings back after a child process exits.
            // The buffer is larger than struct termios on Linux and macOS; cfmakeraw fills it in.
            var termios = new byte[256];
            tcgetattr(0, termios);
            cfmakeraw(termios);
            tcsetattr(0, 0 /* TCSANOW */, termios);
        }
    }

    /// <summary>
    /// Reads raw input. Not through Console on Unix: .NET's console stream puts the terminal back
    /// into canonical mode around each read.
    /// </summary>
    public static int Read(byte[] buffer) =>
        OperatingSystem.IsWindows()
            ? Console.OpenStandardInput().Read(buffer, 0, buffer.Length)
            : (int)read(0, buffer, buffer.Length);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, byte[] buffer, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, byte[] termios);

    [DllImport("libc")]
    private static extern void cfmakeraw(byte[] termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optionalActions, byte[] termios);

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(nint handle, out uint mode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(nint handle, uint mode);
}
