// Tiny programs for Puppetty's end-to-end tests, one per mode:
//
//   echo        "> " prompt; echoes each line back as "echo: <line>"; "quit" exits
//   menu        arrow-key menu; Enter prints "Selected: <item>" and exits with the item's index
//   exit <n>    prints "exiting with <n>" and exits with code n

return args.FirstOrDefault() switch
{
    "echo" => Echo(),
    "menu" => Menu(),
    "exit" => Exit(int.Parse(args[1])),
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
        Console.Write("\u001b[H\u001b[2J");
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

static int Usage()
{
    Console.Error.WriteLine("usage: Puppetty.Fixture echo | menu | exit <n>");
    return 2;
}
