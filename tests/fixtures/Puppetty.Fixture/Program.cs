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

const string Esc = "\u001b";

return args.FirstOrDefault() switch
{
    "echo" => Echo(),
    "menu" => Menu(),
    "exit" => Exit(int.Parse(args[1])),
    "styles" => Styles(),
    "delayed" => Delayed(int.Parse(args[1]), args[2]),
    "sync" => Sync(),
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

static int Usage()
{
    Console.Error.WriteLine("usage: Puppetty.Fixture echo | menu | exit <n> | styles | delayed <ms> <text> | sync");
    return 2;
}
