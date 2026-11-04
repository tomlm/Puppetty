using System.Text;

namespace Puppetty.Cli;

/// <summary>
/// Splits <c>--cmd "python3 -i -q"</c> into a program and arguments. Whitespace separates words;
/// single or double quotes group them. Backslashes are literal, so Windows paths survive.
/// </summary>
internal static class CommandSplitter
{
    public static List<string> Split(string commandLine)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        var inWord = false;
        char? quote = null;
        foreach (var c in commandLine)
        {
            if (quote is { } q)
            {
                if (c == q)
                    quote = null;
                else
                    current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inWord = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inWord)
                {
                    words.Add(current.ToString());
                    current.Clear();
                    inWord = false;
                }
            }
            else
            {
                current.Append(c);
                inWord = true;
            }
        }
        if (quote is not null)
            throw new FormatException($"Unclosed {quote} quote in command: {commandLine}");
        if (inWord)
            words.Add(current.ToString());
        return words;
    }
}
